#import <UIKit/UIKit.h>

// The fake store's payment sheet asks whether the device shows in dark mode, to draw the App Store sheet to match.
extern "C" bool RiseOnIapDarkAppearance() {
    return UITraitCollection.currentTraitCollection.userInterfaceStyle == UIUserInterfaceStyleDark;
}
