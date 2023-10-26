using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using ClickableTransparentOverlay;
using ImGuiNET;

namespace CobaltCoreEditor;

public class Main : Overlay
{
    private const string Title = "ITR's Cobalt Core Editor";
    private const string ProfileFolderHeader = "Profile Folder";
    private const string GameFolderHeader = "Game Folder";
    private const string SelectButton = "Select";

    private const string ProfileHeader = "Profiles";
    private const string ProfileButton = "Slot {0}";

    private const string ExportInfoTitle = "Export Info";
    private const string ExportName = "Ship Name";
    private const string ExportAuthor = "Author";
    private const string ExportDescription = "Description";

    private const string ExportTitle = "Export";
    private const string ExportShip = "Export Ship";
    private const string ExportArtifacts = "Export Artifacts";
    private const string ExportDeck = "Export Deck";
    private const string ExportMap = "Export Map";
    private const string ExportCharacters = "Export Characters & Artifacts";

    private const string ImportTitle = "Import";
    private const string ResetPositon = "Reset position";
    private const string Backup = "Backup";

    private const uint Green = 0xFF00FF00;
    private const uint Red = 0xFF0000FF;
    private const string OpenContainingDirectory = "Open Containing Directory";
    private const string Edit = "Edit";

    private const float WindowWidth = 800;

    private const int QuickImportColumns = 4;


    private string _gameFolderInputText = DataManager.CurrentRootLocation;
    private bool _gameFolderInputValid = DataManager.CurrentRootIsValid;

    private string _rootFolderInputText = ProfileManager.CurrentRootLocation;
    private bool _rootFolderInputValid = ProfileManager.CurrentRootIsValid;

    private bool _folderPickerIsOpen, _filePickerIsOpen;
    private Func<string, bool>? _filePickerTarget;

    private int _pickedProfile = Settings.LastSelectedProfile;

    private bool ProfileSelectionValid =>
        _rootFolderInputValid &&
        _pickedProfile >= 0 &&
        ProfileManager.ValidProfiles[_pickedProfile];

    private bool PathSelectionValid => _gameFolderInputValid && ProfileSelectionValid;

    private ShipMetaData _shipMetaData = new()
    {
        Author = Settings.AuthorName,
        RequiredMods = new List<string>(),
    };

    private bool _exportShip = true, _exportArtifacts, _exportDeck, _exportCharacters, _exportMap;
    private bool _resetPosition = true, _backup = true;

    protected override void Render()
    {
        DataManager.MaybeImport();
        var folderPickerWasOpen = _folderPickerIsOpen;
        var filePickerWasOpen = _filePickerIsOpen;

        ImGui.Begin(Title);

        ImGui.BeginDisabled(_filePickerIsOpen || _folderPickerIsOpen);
        {
            RenderGameFolderBar();
            RenderRootFolderBar();
            RenderProfilePicker();

            ImGui.BeginDisabled(!PathSelectionValid);
            RenderExportInfo();
            ImGui.SameLine();
            RenderExport();
            ImGui.SameLine();
            RenderImport();
            RenderShips();
            ImGui.EndDisabled();
        }
        ImGui.EndDisabled();

        if (_folderPickerIsOpen && !folderPickerWasOpen)
        {
            ImGui.OpenPopup("folder-picker");
        }

        if (_filePickerIsOpen && !filePickerWasOpen)
        {
            ImGui.OpenPopup("file-picker");
        }

        RenderFolderPicker();
        RenderFilePicker();
        ImGui.End();
    }

    private void RenderFolderPicker()
    {
        if (!_folderPickerIsOpen) return;
        if (!ImGui.BeginPopupModal("folder-picker", ref _folderPickerIsOpen, ImGuiWindowFlags.NoTitleBar))
        {
            _folderPickerIsOpen = false;
            ImGui.EndPopup();
            return;
        }

        var picker = FilePicker.GetFolderPicker(this, _rootFolderInputText);
        if (picker.Draw())
        {
            _filePickerTarget?.Invoke(picker.SelectedFile);
            FilePicker.RemoveFilePicker(this);
        }

        ImGui.EndPopup();
    }

    private void RenderFilePicker()
    {
        if (!_filePickerIsOpen) return;
        if (!ImGui.BeginPopupModal("file-picker", ref _filePickerIsOpen, ImGuiWindowFlags.NoTitleBar))
        {
            _filePickerIsOpen = false;
            ImGui.EndPopup();
            return;
        }

        var picker = FilePicker.GetFilePicker(this, DataManager.ModFolderPath, ".ccpj");
        if (picker.Draw())
        {
            _filePickerTarget?.Invoke(picker.SelectedFile);
            FilePicker.RemoveFilePicker(this);
        }

        ImGui.EndPopup();
    }

    private unsafe void RenderGameFolderBar()
    {
        ImGui.Text(GameFolderHeader);
        ImGui.SameLine(150);
        ImGui.PushStyleColor(ImGuiCol.Text, _gameFolderInputValid ? Green : Red);
        ImGui.InputText(
            "##GameFolder",
            ref _gameFolderInputText,
            256,
            ImGuiInputTextFlags.CallbackEdit,
            TrySetGameFolder
        );
        ImGui.PopStyleColor();
        ImGui.SameLine();

        if (!ImGui.Button(SelectButton + "##GameFolder")) return;
        _folderPickerIsOpen = true;
        _filePickerTarget = DataManager.TrySetRootFolder;
    }

    private unsafe void RenderRootFolderBar()
    {
        ImGui.Text(ProfileFolderHeader);
        ImGui.SameLine(150);
        ImGui.PushStyleColor(ImGuiCol.Text, _rootFolderInputValid ? Green : Red);
        ImGui.InputText(
            "##DataFolder",
            ref _rootFolderInputText,
            256,
            ImGuiInputTextFlags.CallbackEdit,
            TrySetRootFolder
        );
        ImGui.PopStyleColor();
        ImGui.SameLine();

        if (!ImGui.Button(SelectButton + "##DataFolder")) return;
        _folderPickerIsOpen = true;
        _filePickerTarget = ProfileManager.TrySetRootFolder;
    }

    private unsafe void RenderProfilePicker()
    {
        ImGui.Text(ProfileHeader);
        ImGui.SameLine(150);

        var unpickedButton = *ImGui.GetStyleColorVec4(ImGuiCol.Button);
        var pickedButton = unpickedButton;
        pickedButton.W = (1 + pickedButton.W) / 2;
        (pickedButton.X, pickedButton.Y, pickedButton.Z) = (pickedButton.Z, pickedButton.X, pickedButton.Y);

        var unpickedHovered = *ImGui.GetStyleColorVec4(ImGuiCol.ButtonHovered);
        var pickedHovered = unpickedHovered;
        (pickedHovered.X, pickedHovered.Y, pickedHovered.Z) =
            (pickedHovered.Z, pickedHovered.X, pickedHovered.Y);

        var unpickedActive = *ImGui.GetStyleColorVec4(ImGuiCol.ButtonActive);
        var pickedActive = unpickedActive;
        (pickedActive.X, pickedActive.Y, pickedActive.Z) = (pickedActive.Z, pickedActive.X, pickedActive.Y);

        for (var i = 0; i < 3; i++)
        {
            ImGui.BeginDisabled(!ProfileManager.ValidProfiles[i] || !_rootFolderInputValid);
            ImGui.PushStyleColor(ImGuiCol.Button, i == _pickedProfile ? pickedButton : unpickedButton);
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, i == _pickedProfile ? pickedHovered : unpickedHovered);
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, i == _pickedProfile ? pickedActive : unpickedActive);

            if (ImGui.Button(string.Format(ProfileButton, i)))
            {
                _pickedProfile = i;
                Settings.LastSelectedProfile = i;
                Settings.Save();
            }

            ImGui.SameLine();

            ImGui.PopStyleColor();
            ImGui.PopStyleColor();
            ImGui.PopStyleColor();

            ImGui.EndDisabled();
        }

        ImGui.BeginDisabled(!ProfileSelectionValid);
        {
            ImGui.SameLine(0, 16);
            if (ImGui.Button(Edit))
            {
                var filepath = ProfileManager.GetSlotPath(_pickedProfile);
                var psi = new ProcessStartInfo
                {
                    FileName = filepath,
                    UseShellExecute = true,
                };
                try
                {
                    Process.Start(psi);
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                }
            }

            ImGui.SameLine();
            if (ImGui.Button(OpenContainingDirectory))
            {
                var filepath = Path.Combine(ProfileManager.CurrentRootLocation, $"Slot{_pickedProfile}");
                FileSystemHelper.Open(filepath);
            }
        }
        ImGui.EndDisabled();
    }

    private void RenderExport()
    {
        ImGui.BeginChild(ExportTitle, new Vector2(WindowWidth / 3, 120));
        ImGui.SeparatorText(ExportTitle);
        ImGui.Checkbox(ExportShip, ref _exportShip);
        ImGui.SameLine();
        ImGui.Checkbox(ExportArtifacts, ref _exportArtifacts);

        ImGui.Checkbox(ExportDeck, ref _exportDeck);
        ImGui.SameLine();
        ImGui.Checkbox(ExportMap, ref _exportMap);


        ImGui.Checkbox(ExportCharacters, ref _exportCharacters);

        ImGui.Spacing();
        ImGui.BeginDisabled(_shipMetaData.Name.Length == 0);
        if (ImGui.Button(ExportTitle))
        {
            var path = ProfileManager.GetSlotPath(_pickedProfile);
            var obj = ImportExport.Import(path);
            var shipName = FileSystemHelper.MakeValidFileName(_shipMetaData.Name);
            var now = FileSystemHelper.Now();
            
            ImportExport.Export(
                Path.Combine(DataManager.ModFolderPath, $"{shipName}_{now}.ccpj"),
                obj,
                _exportShip,
                _exportArtifacts,
                _exportDeck,
                _exportMap,
                _exportCharacters,
                _shipMetaData
            );
            if (Settings.AuthorName != _shipMetaData.Author)
            {
                Settings.AuthorName = _shipMetaData.Author;
                Settings.Save();
            }
        }
        ImGui.EndDisabled();
        ImGui.SameLine();
        if (ImGui.Button(OpenContainingDirectory))
        {
            FileSystemHelper.Open(DataManager.ModFolderPath);
        }
        
        ImGui.EndChild();
    }

    private void RenderExportInfo()
    {
        ImGui.BeginChild(ExportInfoTitle, new Vector2(WindowWidth / 3, 120));
        ImGui.SeparatorText(ExportInfoTitle);

        if (_shipMetaData.Name.Length == 0)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, 0xFF4444FF);
        }
        ImGui.Text(ExportName);
        if (_shipMetaData.Name.Length == 0)
        {
            ImGui.PopStyleColor();
        }
        ImGui.SameLine(70);
        ImGui.InputText(
            "##ExportName",
            ref _shipMetaData.Name,
            80
        );
        ImGui.Text(ExportAuthor);
        ImGui.SameLine(70);
        ImGui.InputText(
            "##ExportAuthor",
            ref _shipMetaData.Author,
            80
        );
        if (_shipMetaData.Description.Length == 0)
        {
            var originalY = ImGui.GetCursorPosY();
            ImGui.SetCursorPos(new Vector2(4, originalY + 4));
            ImGui.Text(ExportDescription);
            ImGui.SameLine();
            ImGui.SetCursorPos(new Vector2(0, originalY));
        }

        ImGui.InputTextMultiline(
            "##ExportDescription",
            ref _shipMetaData.Description,
            512,
            new Vector2(WindowWidth / 3, 50)
        );

        ImGui.EndChild();
    }

    private void RenderImport()
    {
        ImGui.BeginChild(ImportTitle, new Vector2(WindowWidth / 6, 120));
        ImGui.SeparatorText(ImportTitle);
        ImGui.Checkbox(ResetPositon, ref _resetPosition);
        ImGui.Checkbox(Backup, ref _backup);
        ImGui.Spacing();
        if (ImGui.Button(ImportTitle))
        {
            _filePickerIsOpen = true;
            _filePickerTarget = patchPath =>
            {
                try
                {
                    var patchJson = File.ReadAllText(patchPath);
                    ImportExport.Patch(ProfileManager.GetSlotPath(_pickedProfile), patchJson, _resetPosition, _backup);
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                    return false;
                }

                return true;
            };
        }

        ImGui.EndChild();
    }

    private void RenderShips()
    {
        if (!DataManager.CurrentRootIsValid) return;
        var lastIndex = DataManager.Ships.Length - 1;
        for (var i = 0; i <= lastIndex; i++)
        {
            ImGui.PushID(i);
            ImGui.BeginChild("Ship", new Vector2(WindowWidth / QuickImportColumns, 120));
            RenderShip(DataManager.ShipJsons[i], DataManager.Ships[i]);
            ImGui.EndChild();
            if (i < lastIndex && i % QuickImportColumns != QuickImportColumns - 1)
            {
                ImGui.SameLine();
            }

            ImGui.PopID();
        }
    }

    private void RenderShip(in string patchJson, in ShipMetaData shipMetaData)
    {
        ImGui.SeparatorText(shipMetaData.Name);
        ImGui.Text(ExportAuthor);
        ImGui.SameLine();
        var author = shipMetaData.Author;
        var description = shipMetaData.Description;
        ImGui.PushItemWidth(90);
        ImGui.InputText("##auth", ref author, 80, ImGuiInputTextFlags.ReadOnly);
        ImGui.PopItemWidth();
        ImGui.SameLine();
        if (ImGui.Button(ImportTitle))
        {
            ImportExport.Patch(ProfileManager.GetSlotPath(_pickedProfile), patchJson, _resetPosition, _backup);
        }

        ImGui.TextWrapped(description);
    }

    private unsafe int TrySetRootFolder(ImGuiInputTextCallbackData* data)
    {
        if (data->BufTextLen == 0)
        {
            _rootFolderInputValid = false;
            return 0;
        }

        var buffer = new byte[data->BufTextLen];
        Marshal.Copy(new IntPtr(data->Buf), buffer, 0, data->BufTextLen);
        var path = System.Text.Encoding.UTF8.GetString(buffer, 0, data->BufTextLen);

        _rootFolderInputValid = ProfileManager.TrySetRootFolder(path);
        return 0;
    }

    private unsafe int TrySetGameFolder(ImGuiInputTextCallbackData* data)
    {
        if (data->BufTextLen == 0)
        {
            _gameFolderInputValid = false;
            return 0;
        }

        var buffer = new byte[data->BufTextLen];
        Marshal.Copy(new IntPtr(data->Buf), buffer, 0, data->BufTextLen);
        var path = System.Text.Encoding.UTF8.GetString(buffer, 0, data->BufTextLen);

        _gameFolderInputValid = DataManager.TrySetRootFolder(path);
        return 0;
    }
}