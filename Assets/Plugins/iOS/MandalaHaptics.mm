#import <UIKit/UIKit.h>

typedef NS_ENUM(int, MandalaFeedback) {
    MandalaFeedbackSelection = 0,
    MandalaFeedbackPour = 1,
    MandalaFeedbackVacuum = 2,
    MandalaFeedbackSweep = 3,
    MandalaFeedbackRake = 4
};

extern "C" void MandalaPlayHaptic(MandalaFeedback feedback)
{
    dispatch_async(dispatch_get_main_queue(), ^{
        if (UIApplication.sharedApplication.applicationState != UIApplicationStateActive) return;

        static UISelectionFeedbackGenerator *selection;
        static UIImpactFeedbackGenerator *pour;
        static UIImpactFeedbackGenerator *vacuum;
        static UIImpactFeedbackGenerator *sweep;
        static UIImpactFeedbackGenerator *rake;

        switch (feedback) {
            case MandalaFeedbackSelection:
                if (!selection) selection = [UISelectionFeedbackGenerator new];
                [selection selectionChanged];
                break;
            case MandalaFeedbackPour:
                if (!pour) pour = [[UIImpactFeedbackGenerator alloc] initWithStyle:UIImpactFeedbackStyleSoft];
                [pour impactOccurred];
                break;
            case MandalaFeedbackVacuum:
                if (!vacuum) vacuum = [[UIImpactFeedbackGenerator alloc] initWithStyle:UIImpactFeedbackStyleMedium];
                [vacuum impactOccurred];
                break;
            case MandalaFeedbackSweep:
                if (!sweep) sweep = [[UIImpactFeedbackGenerator alloc] initWithStyle:UIImpactFeedbackStyleLight];
                [sweep impactOccurred];
                break;
            case MandalaFeedbackRake:
                if (!rake) rake = [[UIImpactFeedbackGenerator alloc] initWithStyle:UIImpactFeedbackStyleRigid];
                [rake impactOccurred];
                break;
        }
    });
}
