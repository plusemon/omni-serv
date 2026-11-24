---
name: gtk4-libadwaita-theming
description: >-
  Best practices, syntax constraints, and procedures for styling GTK4 and Libadwaita Python applications.
  Use when styling widgets, debugging CSS parser errors, handling dark/light theme switching, or preventing system theme bleed-through.
---

# GTK4 & Libadwaita Theming Guide

## 1. GTK CSS Syntax Rules & Constraints
- **No `!important`**: GTK CSS does not support `!important`. Using it causes parser errors (`Junk at end of value`) and discards the rule.
- **Provider Priority**: Override system or parent styles by loading `Gtk.CssProvider` at high priority:
  ```python
  Gtk.StyleContext.add_provider_for_display(
      Gdk.Display.get_default(),
      provider,
      Gtk.STYLE_PROVIDER_PRIORITY_APPLICATION + 100
  )
  ```
- **Selector Specificity**: If host desktop themes (e.g., Zorin OS, Ubuntu Yaru) inject gradients or images into buttons or rows, use high-specificity selectors with `background-image: none;`:
  ```css
  window.background button.suggested-action,
  window.background .navigation-sidebar > row:selected {
    background-image: none;
    background-color: #0d6efd;
    color: #ffffff;
  }
  ```

## 2. Dynamic Light / Dark Theming
- Avoid hardcoding single-mode background colors without overriding foreground tokens.
- Use `Adw.StyleManager.get_default()` and listen to the `notify::dark` signal:
  ```python
  style_manager = Adw.StyleManager.get_default()
  style_manager.connect("notify::dark", on_dark_changed)
  ```
- Swap theme providers dynamically at runtime with complete color palettes (`window_bg_color`, `window_fg_color`, `card_bg_color`, `card_fg_color`).

## 3. Responsive Column Allocations
- For pages with side-by-side preference groups, override `do_size_allocate` or monitor container width:
  - If width < threshold (e.g. 780px): switch container orientation to `Gtk.Orientation.VERTICAL` (1 column).
  - If width >= threshold: switch container orientation to `Gtk.Orientation.HORIZONTAL` (2 columns).
