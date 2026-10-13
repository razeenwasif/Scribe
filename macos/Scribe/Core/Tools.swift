import Foundation

enum ToolKind: String, CaseIterable, Identifiable {
    case pen
    case highlighter
    /// Removes whole strokes on contact.
    case strokeEraser
    /// Rubs away only the part of a stroke under the tip.
    case pointEraser
    /// Lasso selection of ink and objects.
    case select
    /// Click anywhere to place a text box; objects become editable.
    case text
    /// Drag to move the page.
    case pan

    var id: String { rawValue }

    var isInk: Bool { self == .pen || self == .highlighter }
    var isEraser: Bool { self == .strokeEraser || self == .pointEraser }

    var title: String {
        switch self {
        case .pen: "Pen"
        case .highlighter: "Highlighter"
        case .strokeEraser: "Eraser"
        case .pointEraser: "Partial Eraser"
        case .select: "Lasso Select"
        case .text: "Text"
        case .pan: "Pan"
        }
    }

    var symbol: String {
        switch self {
        case .pen: "pencil.tip"
        case .highlighter: "highlighter"
        case .strokeEraser: "eraser"
        case .pointEraser: "eraser.line.dashed"
        case .select: "lasso"
        case .text: "character.cursor.ibeam"
        case .pan: "hand.raised"
        }
    }

    var shortcut: Character? {
        switch self {
        case .pen: "p"
        case .highlighter: "h"
        case .strokeEraser: "e"
        case .select: "s"
        case .text: "t"
        case .pointEraser, .pan: nil
        }
    }
}

/// One entry in the pen tray. As in OneNote, a pen is a saved combination of
/// colour and thickness rather than two separate settings.
struct Pen: Identifiable, Equatable {
    var name: String
    var highlighter: Bool
    var color: String
    /// Nib width at full pressure, in page units.
    var width: Double

    var id: String { name }
    var pressureSensitive: Bool { !highlighter }

    /// Colours stay legible on the ruled background and remain
    /// distinguishable for the common forms of colour blindness.
    static let tray: [Pen] = [
        Pen(name: "Ink black", highlighter: false, color: "#1A1A1A", width: 2.4),
        Pen(name: "Blue", highlighter: false, color: "#1D4ED8", width: 2.4),
        Pen(name: "Red", highlighter: false, color: "#C02828", width: 2.4),
        Pen(name: "Green", highlighter: false, color: "#157F3C", width: 2.4),
        Pen(name: "Fine black", highlighter: false, color: "#1A1A1A", width: 1.2),
        Pen(name: "Yellow highlighter", highlighter: true, color: "#FFE03D", width: 16),
        Pen(name: "Green highlighter", highlighter: true, color: "#8CE99B", width: 16),
    ]
}
