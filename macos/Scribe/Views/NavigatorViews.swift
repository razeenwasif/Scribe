import SwiftUI

/// Notebooks and their sections.
struct Sidebar: View {
    @Bindable var app: AppModel

    var body: some View {
        List(selection: Binding(get: { app.selectedSectionPath }, set: { app.selectSection($0) })) {
            ForEach(app.notebooks) { nb in
                Section {
                    ForEach(nb.sections) { sec in
                        Label {
                            Text(sec.name).lineLimit(1)
                        } icon: {
                            RoundedRectangle(cornerRadius: 2.5)
                                .fill(Color(nsColor: NSColor(hex: sec.color, fallback: 0x4C9A_FFFF)))
                                .frame(width: 5, height: 15)
                        }
                        .tag(sec.path)
                        .contextMenu { sectionMenu(nb, sec) }
                    }
                } header: {
                    HStack(spacing: 6) {
                        Image(systemName: "book.closed.fill")
                            .foregroundStyle(Color(nsColor: NSColor(hex: nb.color, fallback: 0x7C5C_E6FF)))
                        Text(nb.name).lineLimit(1)
                    }
                    .contextMenu { notebookMenu(nb) }
                }
            }
        }
        .listStyle(.sidebar)
        .safeAreaInset(edge: .bottom) {
            HStack {
                Button {
                    app.prompt = PromptInfo(title: "New Notebook", label: "Notebook name", text: "My Notebook", confirm: "Create") {
                        app.newNotebook(named: $0)
                    }
                } label: {
                    Label("New Notebook", systemImage: "plus")
                }
                .buttonStyle(.borderless)
                Spacer()
            }
            .padding(.horizontal, 12)
            .padding(.vertical, 8)
        }
        .navigationSplitViewColumnWidth(min: 180, ideal: 220, max: 320)
    }

    @ViewBuilder private func notebookMenu(_ nb: NotebookInfo) -> some View {
        Button("New Section…") {
            app.prompt = PromptInfo(title: "New Section", label: "Section name", text: "New Section", confirm: "Create") {
                app.newSection(in: nb, named: $0)
            }
        }
        Button("Rename…") {
            app.prompt = PromptInfo(title: "Rename Notebook", label: "Notebook name", text: nb.name, confirm: "Rename") {
                app.rename(notebook: nb, to: $0)
            }
        }
        Button("Show in Finder") { app.revealInFinder(nb.path) }
        Divider()
        Button("Move to Trash", role: .destructive) {
            app.confirm = ConfirmInfo(
                title: "Move “\(nb.name)” to the Trash?",
                message: "The notebook folder and everything in it go to the Trash, where you can still recover them.",
                action: "Move to Trash"
            ) { app.trash(notebook: nb) }
        }
    }

    @ViewBuilder private func sectionMenu(_ nb: NotebookInfo, _ sec: SectionInfo) -> some View {
        Button("New Page") {
            app.selectSection(sec.path, openFirst: false)
            app.newPage()
        }
        Button("New Section…") {
            app.prompt = PromptInfo(title: "New Section", label: "Section name", text: "New Section", confirm: "Create") {
                app.newSection(in: nb, named: $0)
            }
        }
        Button("Rename…") {
            app.prompt = PromptInfo(title: "Rename Section", label: "Section name", text: sec.name, confirm: "Rename") {
                app.rename(section: sec, to: $0)
            }
        }
        Button("Show in Finder") { app.revealInFinder(sec.path) }
        Divider()
        Button("Move to Trash", role: .destructive) {
            app.confirm = ConfirmInfo(
                title: "Move “\(sec.name)” to the Trash?",
                message: "The section folder and its pages go to the Trash, where you can still recover them.",
                action: "Move to Trash"
            ) { app.trash(section: sec) }
        }
    }
}

/// Pages of the selected section, or search results across every notebook.
struct PageList: View {
    @Bindable var app: AppModel

    var body: some View {
        List(selection: Binding(get: { app.selectedPagePath }, set: { if let p = $0 { app.openPage(p) } })) {
            if let results = app.searchResults {
                if results.isEmpty {
                    Text("No matches").foregroundStyle(.secondary)
                }
                ForEach(results) { row($0, showSection: true) }
            } else {
                ForEach(app.pages) { row($0, showSection: false) }
                    .onMove { app.movePages(from: $0, to: $1) }
            }
        }
        .searchable(text: $app.searchText, placement: .sidebar, prompt: "Search all notebooks")
        .searchFocused($searchFocused)
        .onChange(of: app.focusSearch) { _, now in
            if now {
                searchFocused = true
                app.focusSearch = false
            }
        }
        .overlay {
            if app.searchResults == nil, app.pages.isEmpty, app.selectedSectionPath != nil {
                ContentUnavailableView {
                    Label("No Pages", systemImage: "doc")
                } actions: {
                    Button("New Page") { app.newPage() }
                }
            }
        }
        .navigationTitle(app.section(at: app.selectedSectionPath)?.name ?? "Scribe")
        .navigationSplitViewColumnWidth(min: 200, ideal: 250, max: 380)
        .toolbar {
            ToolbarItem {
                Button { app.newPage() } label: { Label("New Page", systemImage: "square.and.pencil") }
                    .help("New page (⌘N)")
            }
        }
    }

    @FocusState private var searchFocused: Bool

    private func row(_ p: PageInfo, showSection: Bool) -> some View {
        VStack(alignment: .leading, spacing: 2) {
            Text(p.title.isEmpty ? "Untitled page" : p.title)
                .lineLimit(1)
            HStack(spacing: 4) {
                if let d = p.modifiedDate {
                    Text(d, format: .dateTime.day().month(.abbreviated).year())
                }
                if showSection {
                    Text(app.sectionName(for: p.sectionPath)).lineLimit(1)
                }
            }
            .font(.caption)
            .foregroundStyle(.secondary)
        }
        .padding(.vertical, 2)
        .tag(p.path)
        .contextMenu { pageMenu(p) }
    }

    @ViewBuilder private func pageMenu(_ p: PageInfo) -> some View {
        Button("Rename…") {
            app.prompt = PromptInfo(title: "Rename Page", label: "Page title", text: p.title, confirm: "Rename") {
                app.renamePage(p, to: $0)
            }
        }
        Menu("Move To") {
            ForEach(app.notebooks) { nb in
                Section(nb.name) {
                    ForEach(nb.sections) { sec in
                        Button(sec.name) { app.movePage(p, to: sec) }
                            .disabled(sec.path == p.sectionPath)
                    }
                }
            }
        }
        Button("Show in Finder") { app.revealInFinder(p.path) }
        Divider()
        Button("Move to Trash", role: .destructive) {
            app.confirm = ConfirmInfo(
                title: "Move “\(p.title)” to the Trash?",
                message: "You can still recover it from the Trash.",
                action: "Move to Trash"
            ) { app.trashPage(p) }
        }
    }
}

/// A one-field prompt.
struct PromptSheet: View {
    let info: PromptInfo
    @State private var text = ""
    @Environment(\.dismiss) private var dismiss

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            Text(info.title).font(.headline)
            TextField(info.label, text: $text)
                .textFieldStyle(.roundedBorder)
                .frame(width: 300)
                .onSubmit(commit)
            HStack {
                Spacer()
                Button("Cancel", role: .cancel) { dismiss() }
                    .keyboardShortcut(.cancelAction)
                Button(info.confirm, action: commit)
                    .keyboardShortcut(.defaultAction)
                    .disabled(text.trimmingCharacters(in: .whitespaces).isEmpty)
            }
        }
        .padding(20)
        .onAppear { text = info.text }
    }

    private func commit() {
        let t = text.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !t.isEmpty else { return }
        dismiss()
        info.action(t)
    }
}

struct ConfirmInfo: Identifiable {
    let id = UUID()
    var title: String
    var message: String
    var action: String
    var perform: () -> Void
}
