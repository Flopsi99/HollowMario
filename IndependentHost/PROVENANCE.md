# Source and packaging review - 0.10.104

Scope: the current working tree and newly built packages. Historical Git
commits, external backups and previously published releases are not rewritten.
This records engineering provenance evidence, not formal clean-room or legal
clearance. The original bridge was inspected earlier in this project's history;
replacement work must not consult, copy, translate or lightly rewrite it.

| Retained area | Source and review |
| --- | --- |
| IndependentHost/Core | Newly authored native ABI/lifetime/audio wrappers. ABI records and exports follow the public libsm64 header pinned at fd11813208272b4271d92bd92feb8f3fdbe61be5. No original bridge reference or embedded implementation. |
| IndependentHost/Unity and Integration | Newly authored Unity host, collision encoding, rendering, input, audio and control ownership. Uses public libsm64 contracts, Hollow Knight's own interfaces and this companion's behavior requirements. The original assembly is forbidden in package references and runtime checks. |
| World/Terrain | Project-authored connected contour/tessellation pipeline and its room fixtures, retained rather than the copied legacy converter. Calls separately licensed LibTessDotNet source. |
| Shared gameplay/presentation | Project-authored companion hooks, geometry adapters, animation choreography, controls, combat/charm rules and UI. These formerly called the old bridge through reflection; active adapters now resolve our host. No original implementation is imported, embedded or compiled. Old-host conditional alternatives and unused pooling adapters were removed. |
| Compatibility and Setup | Project-authored loader services, hook argument adapter, installer and ROM resolver. API entry loads our host directly. DLL-retargeting builder deleted. Upgrade/removal checks retain retired filenames only to delete or reject them. |
| Assets and text | Creator-supplied replacement artwork and wording, embedded by the shared companion. No original bridge asset bundle is an input. Mario visual/audio assets come from the player's ROM. |
| ThirdParty/LibTessDotNet and official loader libraries | Retained upstream dependencies with their source notices. Official SDK/archive hashes are checked. These are not original-mashup implementations. |
| Regression checks | Owned source fixtures migrated to the replacement host. No old DLL or method-body-preservation comparison is used. Native/integration checks reject the retired loaded assembly. |

Removed inputs: Legacy/Geometry.cs, MarioMath.cs and WorldCollision.cs (their
own README identified these as original 0.1.4 converter copies), the vendored
MarioBridge DLL/native bundle, BridgeBuilder, original decompilation artifacts,
and old local package/source snapshots containing that material. Removal reviews
used filenames, archive entries and assembly metadata, not original method bodies.

Compatibility identifiers such as `MarioHollow.MarioController` are retained as
the existing companion contract, implemented in our own tracked source and
`HollowMario.Host.Integration.dll`. They are not an assembly-loading fallback.
The new host is mandatory for the community API target. Source/package guards prevent the
retired inputs from being restored as build or launch dependencies.

The local Mario runtime remains an upstream libsm64/decompilation build. Removing
the original mashup does not remove that dependency or grant rights to Nintendo
content. It is excluded from public packages and built on the player's machine.
Do not describe this as a legal guarantee or a formal clean-room audit.
