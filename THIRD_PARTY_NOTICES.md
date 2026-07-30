# Third-Party Notices

BaChen AI Launcher is distributed separately from AI plugins, Python
environments, model weights, generated media, and user data. The official
launcher installer must not bundle those items.

The entries below document the locally tested integrations. They are not a
grant of rights and do not replace the upstream license text. Users and
redistributors must review the current upstream terms before downloading or
redistributing a plugin or model.

| Integration | Upstream | Local license evidence | Launcher distribution policy |
| --- | --- | --- | --- |
| Woosh-DFlow | SonyResearch/Woosh | MIT source license, Apache-2.0 components, and Freesound CC BY 3.0/4.0 attribution list | Do not bundle. Preserve all upstream notices when installed or redistributed. |
| Stable Audio 3 integration | Stability-AI/stable-audio-tools | Local integration source is MIT. Model weights and hosted model cards may have separate Stability AI terms. | Do not bundle source, environments, samples, or weights. Require the user to obtain model access separately. |
| IndexTTS2 | index-tts/index-tts | bilibili Model Use License Agreement applies to the model and published code in the inspected local tree. It includes downstream, attribution, scale, prohibited-use, and compliance conditions. | Do not bundle. Show the upstream terms before any future guided download or installation. |
| Managed Python | Python Software Foundation NuGet packages | Python Software Foundation License applies to the downloaded CPython runtime. | Do not bundle. Download the pinned package from NuGet, verify SHA-256, and install it only in the user-selected launcher data directory. |

The launcher uses the Microsoft .NET runtime when published as a self-contained
Windows executable. Applicable Microsoft notices are supplied by the .NET
distribution and remain governed by Microsoft's license terms.

The installer includes the Simplified Chinese language file distributed with
the official Inno Setup source tree. The translation is maintained by Zhenghan
Yang (Kira) and is used under the Inno Setup distribution terms.

Audit basis: local plugin trees inspected on 2026-07-28. Upstream licenses may
change; refresh this audit before every public release that changes supported
plugins or download sources.
