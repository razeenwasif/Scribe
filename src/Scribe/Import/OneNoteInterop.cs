using System.Runtime.InteropServices;

namespace Scribe.Import;

/// <summary>
/// Thrown when the OneNote desktop application cannot be reached.
/// </summary>
public sealed class OneNoteUnavailableException : Exception
{
    public OneNoteUnavailableException(string message, Exception? inner = null)
        : base(message, inner) { }
}

/// <summary>
/// Direct vtable binding to OneNote's IApplication interface.
///
/// Why not the obvious approaches: OneNote's own IDispatch implementation fails
/// with TYPE_E_LIBNOTREGISTERED on real machines, because it cannot locate its
/// type library. That takes out every name-based route at once — <c>dynamic</c>,
/// <c>Type.InvokeMember</c> and raw <c>IDispatch::Invoke</c> all resolve through
/// it and all fail. (Windows PowerShell appears to work only because .NET
/// Framework loads the type library itself; .NET has no such fallback.)
///
/// IApplication is a dual interface, so its methods are also reachable through
/// the vtable, which needs no type library and no name resolution at all. The
/// declaration below therefore mirrors the interface's exact vtable order —
/// which is the one thing that must not be got wrong, since a misaligned slot
/// calls the wrong function pointer. The order was read out of the registered
/// type library; see tools/dump-onenote-vtable.md.
/// </summary>
public sealed class OneNoteInterop : IDisposable
{
    // Hierarchy depth to retrieve.
    public const int ScopeSelf = 0;
    public const int ScopeChildren = 1;
    public const int ScopeNotebooks = 2;
    public const int ScopeSections = 3;
    public const int ScopePages = 4;

    // Page detail. BinaryData is the one that matters: without it, ink and
    // images come back as empty placeholders.
    public const int PageInfoBasic = 0;
    public const int PageInfoBinaryData = 1;

    // Schema version. 2013 is the newest and is what OneNote 2016/365 emit.
    public const int Schema2013 = 2;

    private const int MaxAttempts = 5;

    /// <summary>
    /// OneNote 2013+ IApplication. Every entry is a vtable slot: the four
    /// IDispatch members hold slots 3-6, then IApplication's own methods
    /// follow in declaration order. The unused members are placeholders whose
    /// only job is to occupy their slot, so they must not be removed.
    /// </summary>
    [ComImport]
    [Guid("452ac71a-b655-4967-a208-a4cc39dd7949")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IApplication
    {
        // --- IDispatch, slots 3-6 -----------------------------------------
        void GetTypeInfoCount(out int count);
        void GetTypeInfo(int index, int lcid, out IntPtr typeInfo);
        void GetIDsOfNames(ref Guid riid, IntPtr names, int count, int lcid, IntPtr ids);
        void DispInvoke(int dispId, ref Guid riid, int lcid, short flags,
                        IntPtr args, IntPtr result, IntPtr exception, IntPtr argError);

        // --- IApplication, slot 7 onwards ---------------------------------
        void GetHierarchy([MarshalAs(UnmanagedType.BStr)] string startNodeId,
                          int scope,
                          [MarshalAs(UnmanagedType.BStr)] out string xml,
                          int schema);

        void UpdateHierarchy();      // slot 8
        void OpenHierarchy();        // slot 9
        void DeleteHierarchy();      // slot 10
        void CreateNewPage();        // slot 11
        void CloseNotebook();        // slot 12
        void GetHierarchyParent();   // slot 13

        void GetPageContent([MarshalAs(UnmanagedType.BStr)] string pageId,
                            [MarshalAs(UnmanagedType.BStr)] out string xml,
                            int pageInfo,
                            int schema);
    }

    private IApplication? _app;

    public void Connect()
    {
        var type = Type.GetTypeFromProgID("OneNote.Application");

        if (type is null)
        {
            throw new OneNoteUnavailableException(
                "OneNote does not appear to be installed on this PC.\n\n" +
                "Scribe imports through the OneNote desktop app (the one that ships with " +
                "Office, sometimes listed as “OneNote 2016”). The Store version of OneNote " +
                "does not expose the interface Scribe needs.");
        }

        object instance;

        try
        {
            instance = Activator.CreateInstance(type)
                       ?? throw new OneNoteUnavailableException("OneNote could not be started.");
        }
        catch (COMException ex)
        {
            throw new OneNoteUnavailableException(
                "OneNote is installed but would not start.\n\n" +
                "Try opening OneNote yourself first, then run the import again.", ex);
        }

        try
        {
            _app = (IApplication)instance;
        }
        catch (InvalidCastException ex)
        {
            throw new OneNoteUnavailableException(
                "This version of OneNote does not expose the interface Scribe needs.\n\n" +
                "Scribe supports the OneNote desktop app from Office 2013 onwards.", ex);
        }
    }

    /// <summary>Returns the notebook/section/page tree as XML.</summary>
    public string GetHierarchy(string startNodeId, int scope)
    {
        var app = Require();

        return Retry(() =>
        {
            app.GetHierarchy(startNodeId, scope, out string xml, Schema2013);
            return xml;
        });
    }

    /// <summary>Returns a single page's content as XML, including ink.</summary>
    public string GetPageContent(string pageId, int pageInfo = PageInfoBinaryData)
    {
        var app = Require();

        return Retry(() =>
        {
            app.GetPageContent(pageId, out string xml, pageInfo, Schema2013);
            return xml;
        });
    }

    private IApplication Require() =>
        _app ?? throw new OneNoteUnavailableException("Not connected to OneNote.");

    /// <summary>
    /// OneNote rejects calls while it is syncing or showing a modal prompt.
    /// That is transient, so back off and try again rather than failing an
    /// import part-way through a large notebook.
    /// </summary>
    private static string Retry(Func<string> call)
    {
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                return call();
            }
            catch (COMException ex) when (IsBusy(ex) && attempt < MaxAttempts)
            {
                Thread.Sleep(400 * attempt);
            }
        }

        static bool IsBusy(COMException ex) =>
            ex.HResult is unchecked((int)0x8001010A)   // RPC_E_SERVERCALL_RETRYLATER
                       or unchecked((int)0x80010001);  // RPC_E_CALL_REJECTED
    }

    public void Dispose()
    {
        if (_app is not null && Marshal.IsComObject(_app))
        {
            try
            {
                Marshal.FinalReleaseComObject(_app);
            }
            catch
            {
                // Releasing a dead COM object is not worth surfacing.
            }
        }

        _app = null;
    }
}
