# Repair Commands UI Fix - Installed

User reports oversized clipped labels and unclickable commands in the four-prop desktop scene.

Confirmed code causes:
- Old IMGUI GUIStyle uses fontSize0 with shared dynamic CJK font created at32px, but buttons are only30px minimum high.
- Command buttons require station.ActiveTool, but RepairToolbarView is hidden for cases without repairSteps; these four prop flows use a separate command list rather than core repairSteps.
- IMGUI controls are not GraphicRaycaster targets, unlike the rest of the room/inspection UI.

Installed fix: replace only the prop's context panel with a normal uGUI overlay, explicit typography, scroll viewport, part selector and visible tool controls. Command buttons pick the corresponding tool and then call the original CanExecute/TryExecute guards. ExecuteSelected remains strict for existing code/tests.

User confirmed stopped/saved, and the editor process was already closed. Synced only Runtime/RepairPropStationBridge.cs, newRuntime/RepairPropContextPanel.cs and newUIFixTests/PlayMode. Before sync oldBridge hash matched baseline F8601AEBF17F20C8DA2ACCA435005BD83569843D62C7AF913EFA3E2C60A904C1. No editor force-stop or auto scene rebuild.

Native Unity EditMode54/54, finalPlayMode21/21. Four newUI tests verify actual queued mouse clicks (power/fixture/Dropdown), disabled-operation safety, no model click leakage, scroll without zoom, explicit typography/fixed row height, actual1280x720 and1920x1080. Existing four-order, cosmetics/GPU and VR simulation tests retained.

Screenshots viewed at both true resolutions. Initial640x480 captures remain historical, not resolution acceptance. Fixed row flexibleHeight growth found during visual review. Test-only GameView presets restored/removed after run. Full-suite log still contains EditorXR ProjectValidationWindow ArgumentException entries outside the UI assertions; UI fixture closes that batch-only settings window, but earlier tests can trigger it first. NoXR package source patched and no claim of an error-free full editor log. Game panel/runtime assertions all21 passed; XR editor-window residual remains separate from this UI fix.

Final source scene hashes unchanged: desktop2762B06703944C1D9E21716B277D6FF6E18D298706B5A666538274249A9B1722; VR3C75528859CF169FAB553F6F7FAFBD988864FFAE2AE48AC061EF1262DC7B8662; buildlistEF4BB03EB7C932EA54EFEFEBEEEDBBDC9AFC328FFDD04458DFA1662285BAB1D8.

Main project/model sources/materials untouched. No commit/merge/push. Physical headset and newPlayer build not verified; human UI/play sign-off remains pending. Prior9 unrelated full-project EditMode failures are not claimed fixed.
