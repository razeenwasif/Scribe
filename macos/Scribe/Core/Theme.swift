import AppKit
import SwiftUI

/// Warm palettes. Neutrals lean slightly brown rather than blue: a cool grey
/// UI makes warm paper and black ink look dirty, while a warm one lets the
/// page read as paper even at night.
struct Palette {
    let isDark: Bool
    let backdrop: NSColor
    let page: NSColor
    let pageBorder: NSColor
    let rule: NSColor
    let shadow: NSColor
    let shadowOpacity: Float
    let accent: NSColor
    let accentSoft: NSColor
    let selection: NSColor
    let mutedText: NSColor

    static let dark = Palette(
        isDark: true,
        backdrop: NSColor(hex: "#171614"),
        page: NSColor(hex: "#22211F"),
        pageBorder: NSColor(hex: "#2E2C29"),
        rule: NSColor(hex: "#2E2C29"),
        shadow: .black,
        shadowOpacity: 0.5,
        accent: NSColor(hex: "#E08A66"),
        accentSoft: NSColor(hex: "#3A2A22"),
        selection: NSColor(hex: "#E08A66"),
        mutedText: NSColor(hex: "#A09A90")
    )

    static let light = Palette(
        isDark: false,
        backdrop: NSColor(hex: "#EFEDE6"),
        page: .white,
        pageBorder: NSColor(hex: "#E4E0D6"),
        rule: NSColor(hex: "#E9E4D9"),
        shadow: NSColor(hex: "#4A4238"),
        shadowOpacity: 0.16,
        accent: NSColor(hex: "#B85C3B"),
        accentSoft: NSColor(hex: "#F6E8E1"),
        selection: NSColor(hex: "#B85C3B"),
        mutedText: NSColor(hex: "#6C665E")
    )

    static func of(dark: Bool) -> Palette { dark ? .dark : .light }
}

extension Color {
    /// Terracotta: sits beside ink without competing with it.
    static let scribeAccent = Color(nsColor: NSColor(name: nil) { appearance in
        appearance.bestMatch(from: [.darkAqua, .aqua]) == .darkAqua ? Palette.dark.accent : Palette.light.accent
    })
}

/// Ink colour as painted on the current theme. The stored colour never
/// changes; this only decides what is drawn.
enum InkPaint {
    static func color(hex: String, highlighter: Bool, dark: Bool) -> NSColor {
        var rgba = Core.adapt(Core.parseColor(hex), dark: dark)
        if highlighter {
            // A highlighter must let the ink underneath show through.
            rgba = (rgba & 0xFFFF_FF00) | UInt32(Core.highlighterAlpha(dark: dark))
        }
        return NSColor(rgba: rgba)
    }
}
