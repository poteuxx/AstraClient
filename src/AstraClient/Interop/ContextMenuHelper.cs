using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace AstraClient.Interop;

/// <summary>
/// Helpers for flyout menus. Prefer ToggleButton + Popup (ComboBox pattern) for reclick-safe toggles.
/// Use these helpers only for legacy ContextMenu hosts (e.g. tray icon).
/// </summary>
public static class ContextMenuHelper
{
    /// <summary>
    /// Closes an open ContextMenu when the trigger is re-clicked.
    /// Pair with <see cref="SuppressNextOpenIfClosedOnTrigger"/> on Click.
    /// </summary>
    public static void TryCloseIfOpen(ContextMenu menu, MouseButtonEventArgs e)
    {
        if (!menu.IsOpen) return;
        menu.IsOpen = false;
        e.Handled = true;
    }

    /// <summary>
    /// Call from ContextMenu.Closed — suppresses reopen when the user re-clicked the trigger.
    /// </summary>
    public static void SuppressNextOpenIfClosedOnTrigger(ContextMenu menu, UIElement trigger, ref bool suppressNextOpen)
    {
        if (trigger.IsMouseOver)
            suppressNextOpen = true;
    }

    /// <summary>Opens the menu anchored below the placement target.</summary>
    public static void OpenBelow(ContextMenu menu, UIElement placementTarget, ref bool suppressNextOpen)
    {
        if (suppressNextOpen)
        {
            suppressNextOpen = false;
            return;
        }

        menu.PlacementTarget = placementTarget;
        menu.Placement = PlacementMode.Bottom;
        menu.IsOpen = true;
    }
}
