using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.iOS;
using CoreGraphics;
using Foundation;
using Pixeval.Utilities;
using UIKit;

namespace Pixeval.iOS;

// The UIApplicationDelegate for the application. This class is responsible for launching the 
// User Interface of the application, as well as listening (and optionally responding) to 
// application events from iOS.
[Register("AppDelegate")]
#pragma warning disable CA1711 // Identifiers should not have incorrect suffix
public partial class AppDelegate : AvaloniaAppDelegate<App>
#pragma warning restore CA1711 // Identifiers should not have incorrect suffix
{
    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
    {
        PlatformShareHelper.SharePathAsync = SharePathOnIosAsync;
        return base.CustomizeAppBuilder(builder)
            .WithPixevalFonts();
    }

    private static Task<bool> SharePathOnIosAsync(string path)
    {
        var tcs = new TaskCompletionSource<bool>();
        NSRunLoop.Main.BeginInvokeOnMainThread(() =>
        {
            try
            {
                var items = CollectShareItems(path);
                if (items.Length is 0)
                {
                    tcs.TrySetResult(false);
                    return;
                }

                var presenter = GetTopViewController();
                if (presenter is null)
                {
                    tcs.TrySetResult(false);
                    return;
                }

                var activityController = new UIActivityViewController(items, null);
                if (activityController.PopoverPresentationController is { } popover && presenter.View is { } view)
                {
                    popover.SourceView = view;
                    popover.SourceRect = new CGRect(view.Bounds.GetMidX(), view.Bounds.GetMidY(), 1, 1);
                    popover.PermittedArrowDirections = 0;
                }

                presenter.PresentViewController(activityController, true, null);
                tcs.TrySetResult(true);
            }
            catch
            {
                tcs.TrySetResult(false);
            }
        });
        return tcs.Task;
    }

    private static NSObject[] CollectShareItems(string path)
    {
        if (File.Exists(path))
            return [NSUrl.FromFilename(path)];

        if (Directory.Exists(path))
        {
            var files = Directory.EnumerateFiles(path)
                .Take(50)
                .Select(NSUrl.FromFilename)
                .Cast<NSObject>()
                .ToArray();
            return files.Length > 0 ? files : [NSUrl.FromFilename(path)];
        }

        return [];
    }

    private static UIViewController? GetTopViewController()
    {
        UIWindow? keyWindow = null;
        foreach (var scene in UIApplication.SharedApplication.ConnectedScenes)
        {
            if (scene is UIWindowScene windowScene)
            {
                keyWindow = windowScene.Windows.FirstOrDefault(static w => w.IsKeyWindow)
                    ?? windowScene.Windows.FirstOrDefault();
                if (keyWindow is not null)
                    break;
            }
        }

        var controller = keyWindow?.RootViewController;
        while (controller?.PresentedViewController is { } presented)
            controller = presented;

        return controller;
    }
}
