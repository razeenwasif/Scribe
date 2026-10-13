import SwiftUI

/// The open page: title, tool strip, formatting bar and canvas.
struct EditorView: View {
    @Bindable var app: AppModel
    @Environment(\.undoManager) private var undoManager
    /// Bumped whenever the undo stack changes, which SwiftUI cannot observe.
    @State private var historyTick = 0

    var body: some View {
        Group {
            if let page = app.page {
                VStack(spacing: 0) {
                    PageHeader(app: app, page: page)
                    ToolStrip(app: app, page: page)
                    if app.tool == .text {
                        FormatBar(app: app)
                            .transition(.move(edge: .top).combined(with: .opacity))
                    }
                    PageCanvas(app: app, page: page)
                        .id(ObjectIdentifier(page))
                }
                .animation(.easeOut(duration: 0.15), value: app.tool == .text)
                .onAppear { page.undoManager = undoManager }
                .onChange(of: ObjectIdentifier(page)) {
                    page.undoManager = undoManager
                }
            } else {
                ContentUnavailableView {
                    Label("No Page Open", systemImage: "pencil.and.scribble")
                } description: {
                    Text("Pick a page, or make a new one.")
                } actions: {
                    Button("New Page") { app.newPage() }
                        .keyboardShortcut("n")
                }
            }
        }
        .background(Color(nsColor: Palette.of(dark: app.dark).backdrop))
        .onReceive(NotificationCenter.default.publisher(for: .NSUndoManagerDidUndoChange)) { _ in historyTick += 1 }
        .onReceive(NotificationCenter.default.publisher(for: .NSUndoManagerDidRedoChange)) { _ in historyTick += 1 }
        .toolbar {
            ToolbarItemGroup(placement: .primaryAction) {
                Button { undoManager?.undo() } label: { Label("Undo", systemImage: "arrow.uturn.backward") }
                    .disabled(historyTick < 0 || !(undoManager?.canUndo ?? false))
                    .help("Undo (⌘Z)")
                Button { undoManager?.redo() } label: { Label("Redo", systemImage: "arrow.uturn.forward") }
                    .disabled(!(undoManager?.canRedo ?? false))
                    .help("Redo (⇧⌘Z)")
                Button { app.dark.toggle() } label: {
                    // The icon shows what you will get, not what you have.
                    Label(app.dark ? "Light Page" : "Dark Page", systemImage: app.dark ? "sun.max" : "moon")
                }
                .help(app.dark ? "Switch to light" : "Switch to dark")
            }
        }
    }
}

struct PageHeader: View {
    @Bindable var app: AppModel
    @Bindable var page: PageModel
    @FocusState private var titleFocused: Bool

    var body: some View {
        VStack(alignment: .leading, spacing: 3) {
            TextField("Untitled page", text: $page.title)
                .textFieldStyle(.plain)
                .font(.system(size: 24, weight: .semibold, design: .serif))
                .focused($titleFocused)
                .onChange(of: page.title) { app.titleEdited() }
                .onSubmit { app.commitOpenPage() }
            HStack(spacing: 6) {
                if let created = createdDate {
                    Text(created, format: .dateTime.weekday(.wide).day().month(.wide).year())
                }
                if !app.status.isEmpty {
                    Text("·")
                    Text(app.status)
                }
            }
            .font(.caption)
            .foregroundStyle(.secondary)
        }
        .padding(.horizontal, 22)
        .padding(.top, 14)
        .padding(.bottom, 8)
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(.bar)
        .onChange(of: app.focusTitle) { _, now in
            if now {
                titleFocused = true
                app.focusTitle = false
            }
        }
        .onAppear {
            if app.focusTitle {
                titleFocused = true
                app.focusTitle = false
            }
        }
    }

    private var createdDate: Date? {
        guard let s = page.createdStamp else { return nil }
        return ISO8601DateFormatter.flexible(s)
    }
}

extension ISO8601DateFormatter {
    /// RFC 3339 with or without fractional seconds; the Windows app writes
    /// seven fractional digits, which the stock formatter refuses.
    static func flexible(_ s: String) -> Date? {
        let f = ISO8601DateFormatter()
        f.formatOptions = [.withInternetDateTime]
        if let d = f.date(from: s) { return d }
        let trimmed = s.replacingOccurrences(of: #"\.\d+"#, with: "", options: .regularExpression)
        return f.date(from: trimmed)
    }
}

/// Tools, pens, paper, inserts and zoom.
struct ToolStrip: View {
    @Bindable var app: AppModel
    @Bindable var page: PageModel
    @State private var showWidth = false

    private let mainTools: [ToolKind] = [.pen, .highlighter, .strokeEraser, .select, .text, .pan]

    var body: some View {
        HStack(spacing: 10) {
            HStack(spacing: 2) {
                ForEach(mainTools) { kind in
                    toolButton(kind)
                }
            }
            .padding(3)
            .background(RoundedRectangle(cornerRadius: 8).fill(.quaternary.opacity(0.6)))

            divider

            HStack(spacing: 5) {
                ForEach(Pen.tray) { pen in
                    swatch(pen)
                }
            }

            Button {
                showWidth.toggle()
            } label: {
                HStack(spacing: 4) {
                    Image(systemName: "lineweight")
                    Text(widthLabel).monospacedDigit()
                }
            }
            .buttonStyle(.borderless)
            .disabled(!(app.tool.isInk || app.tool == .pointEraser))
            .popover(isPresented: $showWidth, arrowEdge: .bottom) { widthPopover }
            .help("Nib width")

            divider

            Menu {
                Picker("Paper", selection: paperBinding) {
                    Label("Ruled", systemImage: "line.3.horizontal").tag("ruled")
                    Label("Grid", systemImage: "grid").tag("grid")
                    Label("Dots", systemImage: "circle.grid.3x3").tag("dots")
                    Label("Blank", systemImage: "square").tag("blank")
                }
                .pickerStyle(.inline)
            } label: {
                Label("Paper", systemImage: "doc.plaintext")
            }
            .menuStyle(.borderlessButton)
            .fixedSize()
            .help("Paper")

            Menu {
                Button { app.sheet = .table } label: { Label("Table…", systemImage: "tablecells") }
                Button { app.sheet = .latex(nil) } label: { Label("Equation…", systemImage: "function") }
                Button { app.chooseImage() } label: { Label("Image…", systemImage: "photo") }
                Divider()
                Button { app.paste() } label: { Label("Paste onto Page", systemImage: "doc.on.clipboard") }
            } label: {
                Label("Insert", systemImage: "plus.square.on.square")
            }
            .menuStyle(.borderlessButton)
            .fixedSize()

            Spacer(minLength: 8)

            HStack(spacing: 2) {
                Button { app.canvas?.zoom(by: 1 / 1.25) } label: { Image(systemName: "minus.magnifyingglass") }
                    .help("Zoom out (⌘−)")
                Button { app.canvas?.zoomToActualSize() } label: {
                    Text("\(Int((app.zoom * 100).rounded()))%").monospacedDigit().frame(minWidth: 42)
                }
                .help("Actual size (⌘0)")
                Button { app.canvas?.zoom(by: 1.25) } label: { Image(systemName: "plus.magnifyingglass") }
                    .help("Zoom in (⌘=)")
            }
            .buttonStyle(.borderless)
        }
        .padding(.horizontal, 14)
        .padding(.vertical, 6)
        .background(.bar)
        .overlay(alignment: .bottom) { Divider() }
    }

    private var divider: some View {
        Divider().frame(height: 20)
    }

    private func toolButton(_ kind: ToolKind) -> some View {
        let active = app.tool == kind || (kind == .strokeEraser && app.tool == .pointEraser)
        let symbol = kind == .strokeEraser && app.tool == .pointEraser ? ToolKind.pointEraser.symbol : kind.symbol
        return Button { app.select(tool: kind) } label: {
            Image(systemName: symbol)
                .font(.system(size: 13, weight: .medium))
                .frame(width: 28, height: 24)
                .foregroundStyle(active ? Color.white : Color.primary)
                .background(RoundedRectangle(cornerRadius: 6).fill(active ? Color.scribeAccent : .clear))
                .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .help(help(kind))
    }

    private func help(_ kind: ToolKind) -> String {
        switch kind {
        case .strokeEraser:
            app.tool == .pointEraser
                ? "Partial eraser: rubs out part of a stroke. Click again for whole strokes."
                : "Eraser (E): click again for a partial eraser"
        case .pan: "Pan: or hold Space, or drag with the middle button"
        default: kind.shortcut.map { "\(kind.title) (\(String($0).uppercased()))" } ?? kind.title
        }
    }

    private func swatch(_ pen: Pen) -> some View {
        let current = pen.highlighter ? app.highlighter : app.pen
        let active = current.name == pen.name && (app.tool == (pen.highlighter ? .highlighter : .pen))
        // Show the colour the pen will paint, so the swatch does not promise
        // black and deliver white on a dark page.
        let fill = Color(nsColor: InkPaint.color(hex: pen.color, highlighter: false, dark: app.dark))
        let d: CGFloat = pen.highlighter ? 17 : (pen.width < 2 ? 11 : 14)
        return Button { app.choose(pen: pen) } label: {
            ZStack {
                if pen.highlighter {
                    RoundedRectangle(cornerRadius: 3).fill(fill.opacity(0.85)).frame(width: d, height: d * 0.62)
                } else {
                    Circle().fill(fill).frame(width: d, height: d)
                }
            }
            .frame(width: 22, height: 22)
            .overlay(Circle().strokeBorder(active ? Color.scribeAccent : .clear, lineWidth: 2).frame(width: 22, height: 22))
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .help("\(pen.name) — \(String(format: "%.1f", app.trayWidth(pen))) pt")
    }

    private var currentWidth: Double {
        switch app.tool {
        case .pen: app.pen.width
        case .highlighter: app.highlighter.width
        case .pointEraser: app.eraserWidth
        default: app.pen.width
        }
    }

    private var widthLabel: String { String(format: currentWidth < 10 ? "%.1f" : "%.0f", currentWidth) }

    private var widthPopover: some View {
        let range: ClosedRange<Double> = app.tool == .highlighter ? 4...40 : app.tool == .pointEraser ? 4...60 : 0.5...12
        return VStack(alignment: .leading, spacing: 8) {
            Text("Width").font(.headline)
            Slider(value: Binding(get: { currentWidth }, set: { app.setWidth($0) }), in: range)
                .frame(width: 200)
            Text(String(format: "%.1f pt", currentWidth)).font(.caption).foregroundStyle(.secondary).monospacedDigit()
        }
        .padding(14)
    }

    private var paperBinding: Binding<String> {
        Binding(
            get: { page.background.kind },
            set: { kind in
                let before = page.background
                var after = before
                after.kind = kind
                page.perform("Change Paper", { page.background = after }, undo: { page.background = before })
            }
        )
    }
}

/// Text formatting for the focused text box.
struct FormatBar: View {
    @Bindable var app: AppModel

    private var enabled: Bool { app.textFormat.active && app.activeTextView != nil }

    var body: some View {
        HStack(spacing: 10) {
            Picker("Style", selection: Binding(
                get: { app.textFormat.heading },
                set: { v in HeadingPreset(rawValue: v).map { app.activeTextView?.applyHeading($0) } }
            )) {
                ForEach(HeadingPreset.allCases) { Text($0.label).tag($0.rawValue) }
            }
            .labelsHidden()
            .frame(width: 120)

            Picker("Font", selection: Binding(
                get: { ScribeFonts.canonical(app.textFormat.family) },
                set: { v in app.activeTextView?.format { $0.family = v } }
            )) {
                ForEach(ScribeFonts.choices) { Text($0.label).tag($0.name) }
                if !ScribeFonts.choices.contains(where: { $0.name == ScribeFonts.canonical(app.textFormat.family) }) {
                    Text(app.textFormat.family).tag(ScribeFonts.canonical(app.textFormat.family))
                }
            }
            .labelsHidden()
            .frame(width: 140)

            Picker("Size", selection: Binding(
                get: { app.textFormat.size },
                set: { v in app.activeTextView?.format { $0.size = v } }
            )) {
                ForEach(ScribeFonts.sizes, id: \.self) { Text("\(Int($0))").tag($0) }
                if !ScribeFonts.sizes.contains(app.textFormat.size) {
                    Text(String(format: "%g", app.textFormat.size)).tag(app.textFormat.size)
                }
            }
            .labelsHidden()
            .frame(width: 64)

            ControlGroup {
                toggle("bold", on: app.textFormat.bold, help: "Bold (⌘B)") { $0.bold.toggle() }
                toggle("italic", on: app.textFormat.italic, help: "Italic (⌘I)") { $0.italic.toggle() }
                toggle("underline", on: app.textFormat.underline, help: "Underline (⌘U)") { $0.underline.toggle() }
                toggle("strikethrough", on: app.textFormat.strikethrough, help: "Strikethrough (⇧⌘X)") { $0.strikethrough.toggle() }
            }
            .fixedSize()

            ColorPicker("Colour", selection: Binding(
                get: { Color(nsColor: NSColor(hex: app.textFormat.color)) },
                set: { c in
                    let hex = NSColor(c).hexString
                    app.activeTextView?.format { $0.color = hex }
                }
            ), supportsOpacity: false)
            .labelsHidden()
            .help("Text colour")

            Spacer()

            if !enabled {
                Text("Click in a text box to format it")
                    .font(.caption)
                    .foregroundStyle(.secondary)
            }
        }
        .disabled(!enabled)
        .controlSize(.small)
        .padding(.horizontal, 14)
        .padding(.vertical, 5)
        .background(.bar)
        .overlay(alignment: .bottom) { Divider() }
    }

    private func toggle(_ symbol: String, on: Bool, help: String, _ change: @escaping (inout TextStyle) -> Void) -> some View {
        Button { app.activeTextView?.format(change) } label: {
            Image(systemName: symbol)
                .foregroundStyle(on ? Color.scribeAccent : Color.primary)
        }
        .help(help)
    }
}
