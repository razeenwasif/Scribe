import AppKit
import Observation

/// One stroke on the open page, with its outline cached.
///
/// Rebuilding outlines every frame would make scrolling a page of handwriting
/// crawl, so the path is built once and held until the points change.
final class InkStroke {
    var dto: StrokeDTO {
        didSet { invalidate() }
    }

    private var cachedPath: CGPath?
    private var cachedBounds: CGRect?
    private var cachedPaint: (dark: Bool, color: NSColor)?

    init(_ dto: StrokeDTO) {
        self.dto = dto
    }

    var path: CGPath {
        if let p = cachedPath { return p }
        let p = Core.outlinePath(
            points: dto.points,
            width: max(dto.width, dto.height),
            pressureSensitive: dto.pressureSensitive,
            fit: dto.fit,
            taper: true
        )
        cachedPath = p
        return p
    }

    var bounds: CGRect {
        if let b = cachedBounds { return b }
        let b = Core.strokeBounds(points: dto.points, width: max(dto.width, dto.height))
        cachedBounds = b
        return b
    }

    var nib: Double { max(dto.width, dto.height) }

    func paint(dark: Bool) -> NSColor {
        if let c = cachedPaint, c.dark == dark { return c.color }
        let color = InkPaint.color(hex: dto.color, highlighter: dto.highlighter, dark: dark)
        cachedPaint = (dark, color)
        return color
    }

    private func invalidate() {
        cachedPath = nil
        cachedBounds = nil
        cachedPaint = nil
    }

    func translated(dx: Double, dy: Double) -> StrokeDTO {
        var d = dto
        for i in stride(from: 0, to: d.points.count - 2, by: 3) {
            d.points[i] += dx
            d.points[i + 1] += dy
        }
        return d
    }

    /// Scales about `anchor`. Nib width follows the geometric mean of the two
    /// factors so a stretched selection does not turn hairlines into slabs.
    func scaled(from anchor: CGPoint, sx: Double, sy: Double) -> StrokeDTO {
        var d = dto
        for i in stride(from: 0, to: d.points.count - 2, by: 3) {
            d.points[i] = anchor.x + (d.points[i] - anchor.x) * sx
            d.points[i + 1] = anchor.y + (d.points[i + 1] - anchor.y) * sy
        }
        let k = (abs(sx * sy)).squareRoot()
        d.width = max(0.3, d.width * k)
        d.height = max(0.3, d.height * k)
        return d
    }
}

/// Anything that sits on the page as a box: text, images, equations, tables.
protocol PageObject: AnyObject {
    var objectID: String { get }
    var origin: CGPoint { get set }
    /// Rendered size, reported back by the view that draws the object.
    var size: CGSize { get }
}

extension PageObject {
    var frame: CGRect { CGRect(origin: origin, size: size) }
}

@Observable
final class TextBoxItem: PageObject {
    var dto: TextBoxDTO
    var measuredHeight: CGFloat = 24

    init(_ dto: TextBoxDTO) { self.dto = dto }

    var objectID: String { dto.id }
    var origin: CGPoint {
        get { CGPoint(x: dto.x, y: dto.y) }
        set { dto.x = newValue.x; dto.y = newValue.y }
    }
    var size: CGSize { CGSize(width: dto.width, height: dto.height ?? measuredHeight) }
}

@Observable
final class ImageItem: PageObject {
    var dto: ImageDTO
    @ObservationIgnored private(set) var image: NSImage?

    init(_ dto: ImageDTO, sectionPath: String) {
        self.dto = dto
        let url = URL(fileURLWithPath: sectionPath).appendingPathComponent(dto.file)
        image = NSImage(contentsOf: url)
    }

    var objectID: String { dto.id }
    var origin: CGPoint {
        get { CGPoint(x: dto.x, y: dto.y) }
        set { dto.x = newValue.x; dto.y = newValue.y }
    }
    var size: CGSize { CGSize(width: max(dto.width, 8), height: max(dto.height, 8)) }
}

@Observable
final class LatexItem: PageObject {
    var dto: LatexDTO
    var measured = CGSize(width: 120, height: 40)

    init(_ dto: LatexDTO) { self.dto = dto }

    var objectID: String { dto.id }
    var origin: CGPoint {
        get { CGPoint(x: dto.x, y: dto.y) }
        set { dto.x = newValue.x; dto.y = newValue.y }
    }
    var size: CGSize { measured }
}

@Observable
final class TableItem: PageObject {
    var dto: TableDTO
    var measured = CGSize(width: 300, height: 90)

    init(_ dto: TableDTO) {
        var d = dto
        // Ragged rows (hand-edited files) would break column layout.
        let cols = max(1, d.rows.map(\.count).max() ?? 1)
        d.rows = d.rows.map { $0 + Array(repeating: "", count: cols - $0.count) }
        if d.rows.isEmpty { d.rows = [[""]] }
        self.dto = d
    }

    var objectID: String { dto.id }
    var origin: CGPoint {
        get { CGPoint(x: dto.x, y: dto.y) }
        set { dto.x = newValue.x; dto.y = newValue.y }
    }
    var size: CGSize { measured }

    var columnCount: Int { dto.rows.first?.count ?? 0 }

    static let defaultColumnWidth: Double = 120

    func width(ofColumn c: Int) -> Double {
        guard let w = dto.columnWidths, c < w.count else { return Self.defaultColumnWidth }
        return max(40, w[c])
    }

    func setWidth(_ width: Double, ofColumn c: Int) {
        var w = dto.columnWidths ?? []
        while w.count < columnCount { w.append(Self.defaultColumnWidth) }
        w[c] = max(40, width)
        dto.columnWidths = w
    }
}
