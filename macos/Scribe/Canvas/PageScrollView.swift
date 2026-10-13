import AppKit
import SwiftUI

/// The page viewport: an `NSScrollView` whose magnification gives pinch and
/// ⌘-scroll zoom with correct event coordinates for everything on the page,
/// including the text views inside the SwiftUI object layer.
final class PageScrollView: NSScrollView {
    static let pageMargin: CGFloat = 36

    let surface: PageSurfaceView
    let inkView: InkView
    let objectsHost: NSHostingView<ObjectsLayer>

    private(set) weak var app: AppModel?
    private(set) var page: PageModel

    init(app: AppModel, page: PageModel) {
        self.app = app
        self.page = page
        surface = PageSurfaceView(frame: CGRect(origin: .zero, size: page.pageSize))
        inkView = InkView(frame: CGRect(origin: .zero, size: page.pageSize))
        objectsHost = NSHostingView(rootView: ObjectsLayer(app: app, page: page))
        super.init(frame: .zero)

        contentView = CenteringClipView()
        hasVerticalScroller = true
        hasHorizontalScroller = true
        autohidesScrollers = true
        allowsMagnification = true
        minMagnification = 0.15
        maxMagnification = 6
        drawsBackground = true
        usesPredominantAxisScrolling = false

        let doc = FlippedView(frame: CGRect(
            x: 0, y: 0,
            width: page.pageSize.width + Self.pageMargin * 2,
            height: page.pageSize.height + Self.pageMargin * 2))
        documentView = doc
        doc.addSubview(surface)

        objectsHost.sizingOptions = []
        objectsHost.autoresizingMask = [.width, .height]
        objectsHost.frame = surface.bounds
        surface.addSubview(objectsHost)

        inkView.autoresizingMask = [.width, .height]
        inkView.frame = surface.bounds
        surface.addSubview(inkView)

        inkView.attach(app: app, page: page)
        apply(app: app, page: page)

        NotificationCenter.default.addObserver(
            self, selector: #selector(magnificationChanged), name: NSScrollView.didEndLiveMagnifyNotification, object: self)
    }

    @available(*, unavailable)
    required init?(coder: NSCoder) { fatalError() }

    /// Pushes model state into the AppKit views. Called from SwiftUI updates.
    func apply(app: AppModel, page newPage: PageModel) {
        self.app = app
        if newPage !== page {
            page = newPage
            objectsHost.rootView = ObjectsLayer(app: app, page: newPage)
            inkView.attach(app: app, page: newPage)
            scrollToTop()
        }

        let palette = Palette.of(dark: app.dark)
        backgroundColor = palette.backdrop
        surface.palette = palette
        surface.background = page.background

        let size = page.pageSize
        if surface.frame.size != size {
            surface.frame = CGRect(origin: CGPoint(x: Self.pageMargin, y: Self.pageMargin), size: size)
            documentView?.setFrameSize(CGSize(width: size.width + Self.pageMargin * 2, height: size.height + Self.pageMargin * 2))
            reconstrain()
        } else if surface.frame.origin.x != Self.pageMargin {
            surface.setFrameOrigin(CGPoint(x: Self.pageMargin, y: Self.pageMargin))
        }

        inkView.toolChanged()
        surface.needsDisplay = true
        inkView.needsDisplay = true
    }

    func scrollToTop() {
        contentView.scroll(to: contentView.constrainBoundsRect(CGRect(origin: .zero, size: contentView.bounds.size)).origin)
        reflectScrolledClipView(contentView)
    }

    /// The clip view only re-centres when it scrolls, so nudge it after the
    /// page changes size.
    private func reconstrain() {
        contentView.scroll(to: contentView.constrainBoundsRect(contentView.bounds).origin)
        reflectScrolledClipView(contentView)
    }

    // ----------------------------------------------------------------- zoom

    override func scrollWheel(with event: NSEvent) {
        // ⌘ or ⌃ with the wheel zooms about the pointer, as in most editors.
        if event.modifierFlags.contains(.command) || event.modifierFlags.contains(.control) {
            let delta = event.hasPreciseScrollingDeltas ? event.scrollingDeltaY * 0.01 : event.scrollingDeltaY * 0.08
            let target = (magnification * (1 + delta)).clamped(minMagnification, maxMagnification)
            let center = contentView.convert(event.locationInWindow, from: nil)
            setMagnification(target, centeredAt: center)
            magnificationChanged()
            return
        }
        super.scrollWheel(with: event)
    }

    func zoom(by factor: CGFloat) {
        let center = CGPoint(x: contentView.bounds.midX, y: contentView.bounds.midY)
        setMagnification((magnification * factor).clamped(minMagnification, maxMagnification), centeredAt: center)
        magnificationChanged()
    }

    func zoomToActualSize() {
        let center = CGPoint(x: contentView.bounds.midX, y: contentView.bounds.midY)
        setMagnification(1, centeredAt: center)
        magnificationChanged()
    }

    /// Fits the page width to the window.
    func zoomToFit() {
        let target = (contentView.frame.width / (page.pageSize.width + Self.pageMargin * 2)).clamped(minMagnification, maxMagnification)
        magnification = target
        magnificationChanged()
    }

    @objc private func magnificationChanged() {
        app?.zoom = magnification
        inkView.needsDisplay = true
    }

    /// Centre of the visible part of the page, in page coordinates.
    var visiblePageCenter: CGPoint {
        let visible = surface.convert(contentView.bounds, from: contentView)
        let r = visible.intersection(surface.bounds)
        guard !r.isNull else { return CGPoint(x: 120, y: 120) }
        return CGPoint(x: r.midX, y: max(r.minY + 40, r.midY - 120))
    }
}

/// Keeps the page centred when it is narrower than the window.
final class CenteringClipView: NSClipView {
    override func constrainBoundsRect(_ proposedBounds: NSRect) -> NSRect {
        var rect = super.constrainBoundsRect(proposedBounds)
        guard let doc = documentView else { return rect }
        if rect.width > doc.frame.width {
            rect.origin.x = (doc.frame.width - rect.width) / 2
        }
        if rect.height > doc.frame.height {
            rect.origin.y = (doc.frame.height - rect.height) / 2
        }
        return rect
    }

    override var isFlipped: Bool { true }
}

final class FlippedView: NSView {
    override var isFlipped: Bool { true }
}

/// The sheet of paper: fill, rule lines, and a soft shadow.
///
/// Rules are drawn in page coordinates rather than tiled, so the spacing
/// stays locked to the page at every zoom and handwriting keeps sitting on
/// the same line.
final class PageSurfaceView: NSView {
    var palette = Palette.dark {
        didSet {
            applyChrome()
            needsDisplay = true
        }
    }

    var background = BackgroundDTO() {
        didSet { if background != oldValue { needsDisplay = true } }
    }

    override init(frame: NSRect) {
        super.init(frame: frame)
        wantsLayer = true
        layer?.masksToBounds = false
        applyChrome()
    }

    @available(*, unavailable)
    required init?(coder: NSCoder) { fatalError() }

    override var isFlipped: Bool { true }

    private func applyChrome() {
        guard let layer else { return }
        layer.shadowColor = palette.shadow.cgColor
        layer.shadowOpacity = palette.shadowOpacity
        layer.shadowRadius = 14
        layer.shadowOffset = CGSize(width: 0, height: -4)
        layer.borderColor = palette.pageBorder.cgColor
        layer.borderWidth = 1
    }

    override func layout() {
        super.layout()
        // An explicit path spares the compositor from deriving the shadow
        // from the page's pixels on every frame.
        layer?.shadowPath = CGPath(rect: bounds, transform: nil)
    }

    override func draw(_ dirtyRect: NSRect) {
        palette.page.setFill()
        dirtyRect.fill()

        let kind = background.kind.lowercased()
        guard kind != "blank" else { return }

        let spacing = max(background.spacing, 4)
        // Dark mode supplies its own rule colour so the lines do not glare;
        // the stored light-mode colour is left untouched.
        let rule = palette.isDark ? palette.rule : NSColor(hex: background.color, fallback: 0xE8EE_F5FF)
        rule.set()

        let first = Int(floor(dirtyRect.minY / spacing))
        let last = Int(ceil(dirtyRect.maxY / spacing))

        if kind == "dots" {
            let firstX = Int(floor(dirtyRect.minX / spacing))
            let lastX = Int(ceil(dirtyRect.maxX / spacing))
            let dots = NSBezierPath()
            for j in max(first, 1)...max(last, 1) {
                for i in max(firstX, 1)...max(lastX, 1) {
                    dots.appendOval(in: CGRect(x: Double(i) * spacing - 1.1, y: Double(j) * spacing - 1.1, width: 2.2, height: 2.2))
                }
            }
            dots.fill()
            return
        }

        let lines = NSBezierPath()
        lines.lineWidth = 1
        for j in max(first, 1)...max(last, 1) {
            let y = Double(j) * spacing
            lines.move(to: CGPoint(x: dirtyRect.minX, y: y))
            lines.line(to: CGPoint(x: dirtyRect.maxX, y: y))
        }

        if kind == "grid" {
            let firstX = Int(floor(dirtyRect.minX / spacing))
            let lastX = Int(ceil(dirtyRect.maxX / spacing))
            for i in max(firstX, 1)...max(lastX, 1) {
                let x = Double(i) * spacing
                lines.move(to: CGPoint(x: x, y: dirtyRect.minY))
                lines.line(to: CGPoint(x: x, y: dirtyRect.maxY))
            }
        } else if kind == "ruled" {
            // The margin rule, as on paper.
            let x = spacing * 3
            lines.move(to: CGPoint(x: x, y: dirtyRect.minY))
            lines.line(to: CGPoint(x: x, y: dirtyRect.maxY))
        }
        lines.stroke()
    }
}

extension Comparable {
    func clamped(_ lo: Self, _ hi: Self) -> Self { min(max(self, lo), hi) }
}

/// SwiftUI wrapper for the canvas.
struct PageCanvas: NSViewRepresentable {
    let app: AppModel
    let page: PageModel

    func makeNSView(context: Context) -> PageScrollView {
        let view = PageScrollView(app: app, page: page)
        app.canvas = view
        return view
    }

    func updateNSView(_ view: PageScrollView, context: Context) {
        // Reading these registers them for observation, so the canvas is
        // refreshed when any of them change.
        _ = (app.dark, app.tool, page.pageSize, page.background, page.selectedObjects)
        app.canvas = view
        view.apply(app: app, page: page)
    }
}
