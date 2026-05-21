// Kneeboard Viewer - a viewer for DCS World multiplayer mission kneeboards.
// Copyright (C) 2026 David Klasinc
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.
// See the LICENSE file in the project root for the full license text.

using BarRaider.SdTools;

namespace KneeboardViewer.StreamDeck;

/// <summary>
/// Common no-op plumbing for our fire-and-forget key actions. Each KeyPressed
/// calls TrySend/RunOrShow and ignores the result: if the viewer is not running
/// the key simply does nothing, which is the intended behavior.
/// </summary>
public abstract class KneeboardActionBase : KeypadBase
{
    protected KneeboardActionBase(ISDConnection connection, InitialPayload payload)
        : base(connection, payload) { }

    public override void KeyReleased(KeyPayload payload) { }
    public override void OnTick() { }
    public override void ReceivedSettings(ReceivedSettingsPayload payload) { }
    public override void ReceivedGlobalSettings(ReceivedGlobalSettingsPayload payload) { }
    public override void Dispose() { }
}

[PluginActionId("com.bigwhale.kneeboardviewer.nextpage")]
public class NextPageAction : KneeboardActionBase
{
    public NextPageAction(ISDConnection c, InitialPayload p) : base(c, p) { }
    public override void KeyPressed(KeyPayload payload) => KneeboardClient.TrySend("next-page");
}

[PluginActionId("com.bigwhale.kneeboardviewer.prevpage")]
public class PrevPageAction : KneeboardActionBase
{
    public PrevPageAction(ISDConnection c, InitialPayload p) : base(c, p) { }
    public override void KeyPressed(KeyPayload payload) => KneeboardClient.TrySend("prev-page");
}

[PluginActionId("com.bigwhale.kneeboardviewer.nexttab")]
public class NextTabAction : KneeboardActionBase
{
    public NextTabAction(ISDConnection c, InitialPayload p) : base(c, p) { }
    public override void KeyPressed(KeyPayload payload) => KneeboardClient.TrySend("next-tab");
}

[PluginActionId("com.bigwhale.kneeboardviewer.prevtab")]
public class PrevTabAction : KneeboardActionBase
{
    public PrevTabAction(ISDConnection c, InitialPayload p) : base(c, p) { }
    public override void KeyPressed(KeyPayload payload) => KneeboardClient.TrySend("prev-tab");
}

[PluginActionId("com.bigwhale.kneeboardviewer.reload")]
public class ReloadAction : KneeboardActionBase
{
    public ReloadAction(ISDConnection c, InitialPayload p) : base(c, p) { }
    public override void KeyPressed(KeyPayload payload) => KneeboardClient.TrySend("reload");
}

[PluginActionId("com.bigwhale.kneeboardviewer.quit")]
public class QuitAction : KneeboardActionBase
{
    public QuitAction(ISDConnection c, InitialPayload p) : base(c, p) { }
    public override void KeyPressed(KeyPayload payload) => KneeboardClient.TrySend("quit");
}

[PluginActionId("com.bigwhale.kneeboardviewer.run")]
public class RunAction : KneeboardActionBase
{
    public RunAction(ISDConnection c, InitialPayload p) : base(c, p) { }
    public override void KeyPressed(KeyPayload payload) => KneeboardClient.RunOrShow();
}
