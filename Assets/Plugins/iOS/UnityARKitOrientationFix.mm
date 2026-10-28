#import <UIKit/UIKit.h>
#import <objc/runtime.h>

// Unity's ARKit plugin reads deprecated statusBarOrientation; iOS 27 SDK builds get Unknown (UUM-154331).
// Fall back to the window scene orientation so the plugin keeps the portrait roll and projection.

static IMP g_originalStatusBarOrientation = NULL;
static UIInterfaceOrientation g_lastKnownOrientation = UIInterfaceOrientationUnknown;

static UIInterfaceOrientation ARKitFixSceneInterfaceOrientation(void)
{
    UIWindowScene* fallback = nil;
    for (UIScene* scene in UIApplication.sharedApplication.connectedScenes) {
        if (![scene isKindOfClass:[UIWindowScene class]]) continue;
        UIWindowScene* ws = (UIWindowScene*)scene;
        if (ws.activationState == UISceneActivationStateForegroundActive) return ws.interfaceOrientation;
        if (fallback == nil) fallback = ws;
    }
    return fallback ? fallback.interfaceOrientation : UIInterfaceOrientationUnknown;
}

static UIInterfaceOrientation ARKitFixPatchedStatusBarOrientation(id self, SEL _cmd)
{
    UIInterfaceOrientation original = UIInterfaceOrientationUnknown;
    if (g_originalStatusBarOrientation) {
        original = ((UIInterfaceOrientation (*)(id, SEL))g_originalStatusBarOrientation)(self, _cmd);
    }
    if (original != UIInterfaceOrientationUnknown) {
        g_lastKnownOrientation = original;
        return original;
    }
    if (NSThread.isMainThread) {
        UIInterfaceOrientation fromScene = ARKitFixSceneInterfaceOrientation();
        if (fromScene != UIInterfaceOrientationUnknown) g_lastKnownOrientation = fromScene;
    }
    return g_lastKnownOrientation;
}

__attribute__((constructor))
static void ARKitFixInstallStatusBarOrientationCompat(void)
{
    Method m = class_getInstanceMethod([UIApplication class], @selector(statusBarOrientation));
    if (m == NULL) return;
    g_originalStatusBarOrientation = method_getImplementation(m);
    method_setImplementation(m, (IMP)ARKitFixPatchedStatusBarOrientation);
    NSLog(@"[UnityARKitOrientationFix] statusBarOrientation compat installed");
}
