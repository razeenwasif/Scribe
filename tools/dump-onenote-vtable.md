# Re-deriving OneNote's IApplication vtable

`src/Scribe/Import/OneNoteInterop.cs` calls OneNote through its **vtable**, not
by method name. That makes the declaration order in `IApplication` load-bearing:
a misaligned slot calls the wrong function pointer. This note records why, and
how to regenerate the order if a future OneNote changes it.

## Why not bind by name

Every name-based route fails on real machines:

| Approach | Result |
|----------|--------|
| `dynamic` | `E_FAIL (0x80004005)` |
| `Type.InvokeMember` | `TYPE_E_LIBNOTREGISTERED (0x8002801D)` |
| Raw `IDispatch::Invoke` with a DISPID from the typelib | `TYPE_E_LIBNOTREGISTERED` |
| `IDispatch::GetIDsOfNames` directly | `TYPE_E_LIBNOTREGISTERED` |

OneNote's own `IDispatch` implementation cannot locate its type library, so
everything that resolves through it fails — including calls where the DISPID was
already known, which rules out name resolution being the problem.

Windows PowerShell works because .NET Framework loads the type library itself
and dispatches from that. .NET has no equivalent fallback.

`IApplication` is a **dual** interface, so its methods are also reachable
through the vtable. That path needs no type library, no name resolution, and no
`IDispatch` at all.

## Regenerating the order

The type library *is* registered even though OneNote cannot use it, so it can be
read directly. Run this as a `net10.0-windows` console app on a machine with
OneNote installed:

```csharp
using System.Runtime.InteropServices;
using CT = System.Runtime.InteropServices.ComTypes;

[DllImport("oleaut32.dll", PreserveSig = false)]
static extern void LoadRegTypeLib(ref Guid guid, short major, short minor, int lcid,
                                  out CT.ITypeLib tlb);

// "Microsoft OneNote 15.0 Type Library"
var guid = new Guid("0EA692EE-BB50-4E3C-AEF0-356D91732725");
LoadRegTypeLib(ref guid, 1, 1, 0, out var tlb);

for (int i = 0; i < tlb.GetTypeInfoCount(); i++)
{
    tlb.GetTypeInfo(i, out CT.ITypeInfo ti);
    ti.GetDocumentation(-1, out string name, out _, out _, out _);
    if (name != "IApplication") continue;

    ti.GetTypeAttr(out IntPtr pAttr);
    var attr = Marshal.PtrToStructure<CT.TYPEATTR>(pAttr);
    Console.WriteLine($"IID = {attr.guid}");
    ti.ReleaseTypeAttr(pAttr);

    for (int f = 0; f < attr.cFuncs; f++)
    {
        ti.GetFuncDesc(f, out IntPtr pFd);
        var fd = Marshal.PtrToStructure<CT.FUNCDESC>(pFd);
        ti.GetDocumentation(fd.memid, out string fn, out _, out _, out _);
        Console.WriteLine($"slot {fd.oVft / 8,2}  params {fd.cParams}  {fn}");
        ti.ReleaseFuncDesc(pFd);
    }
    break;
}
```

`oVft` is a byte offset; divide by 8 for the 64-bit slot index. (The four
inherited `IDispatch` entries report 4-byte offsets because they come from the
32-bit base type info — ignore those and trust the derived methods.)

## The order as of OneNote 2016 / 365

IID `452ac71a-b655-4967-a208-a4cc39dd7949`, 36 functions.

Slots 0-2 are `IUnknown`, 3-6 are `IDispatch`, then:

| Slot | Method | Signature (as used) |
|------|--------|---------------------|
| 7 | `GetHierarchy` | `(BSTR startNodeId, enum scope, out BSTR xml, enum schema)` |
| 8 | `UpdateHierarchy` | |
| 9 | `OpenHierarchy` | |
| 10 | `DeleteHierarchy` | |
| 11 | `CreateNewPage` | |
| 12 | `CloseNotebook` | |
| 13 | `GetHierarchyParent` | |
| 14 | `GetPageContent` | `(BSTR pageId, out BSTR xml, enum pageInfo, enum schema)` |
| 15 | `UpdatePageContent` | |
| 16 | `GetBinaryPageContent` | |
| 17 | `DeletePageContent` | |
| 18 | `NavigateTo` | |
| 19 | `NavigateToUrl` | |
| 20 | `Publish` | |
| 21 | `OpenPackage` | |
| 22 | `GetHyperlinkToObject` | |
| 23 | `FindPages` | |
| 24 | `FindMeta` | |
| 25 | `GetSpecialLocation` | |
| 26 | `MergeFiles` | |
| 27 | `QuickFiling` | |
| 28 | `SyncHierarchy` | |
| 29 | `SetFilingLocation` | |
| 30 | `Windows` | |
| 31 | `Dummy1` | |
| 32 | `MergeSections` | |
| 33 | `COMAddIns` | |
| 34 | `LanguageSettings` | |
| 35 | `GetWebHyperlinkToObject` | |

Scribe declares only through slot 14 and stubs the gaps, since it reads and
never writes. If you add a call past slot 14, extend the declaration with
placeholders up to it — do not renumber.
