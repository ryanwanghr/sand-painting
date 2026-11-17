using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Mandala.Game
{
    /// <summary>Owns captured pointers; a second finger suspends painting until all fingers lift.</summary>
    public sealed class TrayGesture : PointerManipulator
    {
        readonly Dictionary<int, Vector2> pointers = new Dictionary<int, Vector2>();
        readonly VisualElement surface;
        readonly float touchTipOffset;
        public Rect View { get; private set; } = new Rect(0, 0, 1, 1);
        public Action<Vector2> Begin, Move;
        public Action End;
        public float Zoom { get; private set; } = 1;
        public Vector2 TipPosition { get; private set; }
        public bool TouchTipVisible => painting && strokeOffset > 0;
        public bool ToolVisible => painting || hovering;
        Vector2 center = new Vector2(.5f, .5f);
        float strokeOffset;
        bool gesture, painting, hovering;
        public TrayGesture(VisualElement surface = null, float touchTipOffset = 64)
        {
            this.surface = surface;
            this.touchTipOffset = Mathf.Max(0, touchTipOffset);
        }
        VisualElement Surface => surface ?? target;
        protected override void RegisterCallbacksOnTarget()
        {
            target.RegisterCallback<PointerDownEvent>(Down); target.RegisterCallback<PointerMoveEvent>(Motion);
            target.RegisterCallback<PointerUpEvent>(Up); target.RegisterCallback<PointerCancelEvent>(Cancel);
            target.RegisterCallback<PointerCaptureOutEvent>(Lost); target.RegisterCallback<WheelEvent>(Wheel);
            target.RegisterCallback<PointerEnterEvent>(Enter); target.RegisterCallback<PointerLeaveEvent>(Leave);
        }
        protected override void UnregisterCallbacksFromTarget()
        {
            CancelAll(); target.UnregisterCallback<PointerDownEvent>(Down); target.UnregisterCallback<PointerMoveEvent>(Motion);
            target.UnregisterCallback<PointerUpEvent>(Up); target.UnregisterCallback<PointerCancelEvent>(Cancel);
            target.UnregisterCallback<PointerCaptureOutEvent>(Lost); target.UnregisterCallback<WheelEvent>(Wheel);
            target.UnregisterCallback<PointerEnterEvent>(Enter); target.UnregisterCallback<PointerLeaveEvent>(Leave);
        }
        Vector2 Local(Vector3 position) => Surface.WorldToLocal(position);
        Vector2 Normalized(Vector2 local) => new Vector2(local.x / Mathf.Max(1, Surface.contentRect.width), local.y / Mathf.Max(1, Surface.contentRect.height));
        Vector2 Cell(Vector2 local) => (center + (Normalized(local) - Vector2.one * .5f) / Zoom) * SandTray.Size;
        void Hover(Vector2 point)
        {
            TipPosition = point;
            hovering = Surface.contentRect.Contains(point);
        }
        void Enter(PointerEnterEvent e)
        {
            if (pointers.Count == 0 && e.pointerType != UnityEngine.UIElements.PointerType.touch) Hover(Local(e.position));
        }
        void Leave(PointerLeaveEvent e) { if (e.pointerType != UnityEngine.UIElements.PointerType.touch) hovering = false; }
        void Paint(Vector2 finger)
        {
            TipPosition = finger - Vector2.up * strokeOffset;
            if (!Surface.contentRect.Contains(TipPosition)) { Finish(); return; }
            if (!painting) { painting = true; Begin?.Invoke(Cell(TipPosition)); }
            else Move?.Invoke(Cell(TipPosition));
        }
        void Down(PointerDownEvent e)
        {
            if (e.button != 0 || pointers.Count >= 2 || pointers.ContainsKey(e.pointerId)) return;
            hovering = false;
            pointers[e.pointerId] = Local(e.position); target.CapturePointer(e.pointerId);
            if (pointers.Count == 1 && !gesture)
            {
                strokeOffset = e.pointerType == UnityEngine.UIElements.PointerType.touch ? touchTipOffset : 0;
                Paint(pointers[e.pointerId]);
            }
            else { gesture = true; Finish(); }
            e.StopPropagation();
        }
        void Pair(out Vector2 a, out Vector2 b)
        {
            var enumerator = pointers.Values.GetEnumerator(); enumerator.MoveNext(); a = enumerator.Current; enumerator.MoveNext(); b = enumerator.Current;
        }
        void Motion(PointerMoveEvent e)
        {
            if (!pointers.ContainsKey(e.pointerId))
            {
                if (pointers.Count == 0 && e.pointerType != UnityEngine.UIElements.PointerType.touch) Hover(Local(e.position));
                return;
            }
            if (pointers.Count == 2)
            {
                Pair(out var a, out var b); Vector2 oldMiddle = (a + b) * .5f;
                float oldDistance = Vector2.Distance(a, b);
                Vector2 anchor = center + (Normalized(oldMiddle) - Vector2.one * .5f) / Zoom;
                pointers[e.pointerId] = Local(e.position); Pair(out a, out b);
                if (oldDistance > 1) Zoom = Mathf.Clamp(Zoom * Vector2.Distance(a, b) / oldDistance, 1, 4);
                center = anchor - (Normalized((a + b) * .5f) - Vector2.one * .5f) / Zoom; Refresh();
            }
            else { pointers[e.pointerId] = Local(e.position); if (!gesture) Paint(pointers[e.pointerId]); }
            e.StopPropagation();
        }
        void Up(PointerUpEvent e)
        {
            Release(e.pointerId);
            if (pointers.Count == 0 && e.pointerType != UnityEngine.UIElements.PointerType.touch) Hover(Local(e.position));
            e.StopPropagation();
        }
        void Cancel(PointerCancelEvent e) { if (pointers.ContainsKey(e.pointerId)) CancelAll(); }
        void Lost(PointerCaptureOutEvent e) { if (pointers.ContainsKey(e.pointerId)) CancelAll(); }
        void Release(int id)
        {
            if (!pointers.Remove(id)) return; Finish(); target.ReleasePointer(id);
            if (pointers.Count == 0) gesture = false;
        }
        void Finish() { if (painting) { painting = false; End?.Invoke(); } }
        public void CancelAll()
        {
            Finish(); hovering = false; var ids = new List<int>(pointers.Keys); pointers.Clear();
            foreach (int id in ids) target.ReleasePointer(id); gesture = false;
        }
        void Wheel(WheelEvent e)
        {
            if (pointers.Count > 0) return;
            Vector2 point = Normalized(Local(e.mousePosition)) - Vector2.one * .5f;
            Vector2 anchor = center + point / Zoom;
            Zoom = Mathf.Clamp(Zoom * Mathf.Exp(-e.delta.y * .06f), 1, 4); center = anchor - point / Zoom; Refresh(); e.StopPropagation();
        }
        public void ResetView() { CancelAll(); Zoom = 1; center = Vector2.one * .5f; Refresh(); }
        void Refresh()
        {
            float half = .5f / Zoom; center.x = Mathf.Clamp(center.x, half, 1 - half); center.y = Mathf.Clamp(center.y, half, 1 - half);
            View = new Rect(center.x - half, 1 - center.y - half, half * 2, half * 2);
        }
    }
}
