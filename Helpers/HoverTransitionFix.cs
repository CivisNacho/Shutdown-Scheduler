using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System.Collections.Generic;

namespace Shutdown_Scheduler_WinUI.Helpers
{
    // WinUI and CommunityToolkit templates animate Background with a BrushTransition, so the opaque brand
    // brushes cross-fade through a bright intermediate frame on hover. Clearing the transition on the
    // template elements makes the hover colour apply immediately. The toolkit templates are compiled, so
    // this is done at runtime instead of editing the templates.
    internal static class HoverTransitionFix
    {
        // Attaches once per root. Controls created later (lazy SettingsExpander items, ItemsControl rows)
        // are stripped on their first hover.
        public static void Attach(UIElement root)
        {
            root.AddHandler(UIElement.PointerEnteredEvent, new PointerEventHandler(OnPointerEntered), handledEventsToo: true);
        }

        // Walks the visual tree under root and clears background transitions on every Panel and Border.
        public static void StripAll(DependencyObject root)
        {
            var stack = new Stack<DependencyObject>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                var current = stack.Pop();
                StripElement(current);

                int count = VisualTreeHelper.GetChildrenCount(current);
                for (int i = 0; i < count; i++)
                {
                    stack.Push(VisualTreeHelper.GetChild(current, i));
                }
            }
        }

        private static void OnPointerEntered(object sender, PointerRoutedEventArgs e)
        {
            // Walk up from the hovered element, stripping each ancestor up to the root.
            DependencyObject? current = e.OriginalSource as DependencyObject;
            while (current is not null)
            {
                StripElement(current);
                if (ReferenceEquals(current, sender))
                {
                    break;
                }

                current = VisualTreeHelper.GetParent(current);
            }
        }

        private static void StripElement(DependencyObject element)
        {
            switch (element)
            {
                case Panel panel:
                    panel.BackgroundTransition = null;
                    break;
                case Border border:
                    border.BackgroundTransition = null;
                    break;
            }
        }
    }
}
