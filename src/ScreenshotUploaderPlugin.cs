using System;
using System.IO;
using System.Reflection;
using System.Threading;
using Assets.Scripts.UI.Sharing;
using BepInEx;
using HarmonyLib;
using Jundroo.Juicy.Widgets;
using Ookii.Dialogs;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using WinForms = System.Windows.Forms;

namespace SP2ScreenshotUploader;

// Adds an "Upload Screenshot" button to the craft-upload dialog, right below the
// native "Take Screenshot" (add-screenshot) button. It opens a native file picker,
// loads the chosen PNG/JPG, and injects it into the dialog's screenshot list exactly
// the way the game's own OnAddScreenshotButtonClicked callback does -- so the image
// rides along in the UploadCraft POST as a "UserView" attachment.
[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public sealed class ScreenshotUploaderPlugin : BaseUnityPlugin
{
    public const string PluginGuid = "codex.sp2.screenshotuploader";
    public const string PluginName = "SP2 Screenshot Uploader";
    public const string PluginVersion = "0.1.0";

    // Matches the private MaxScreenshots constant in UploadDialogScript.
    private const int MaxScreenshots = 3;

    // Called via reflection: the ImageConversion.LoadImage extension also has a
    // ReadOnlySpan overload that the net48 compiler can't resolve, so bind the
    // byte[] overload directly.
    private static readonly MethodInfo LoadImageMethod =
        typeof(ImageConversion).GetMethod("LoadImage", new[] { typeof(Texture2D), typeof(byte[]) });

    private const string UploadLabel = "Upload Screenshot";
    private const string CloneName = "upload-screenshot-button";

    private UploadDialogScript _dialog;
    private ButtonWidget _addButton;     // native "Take Screenshot" button (template + size source)
    private ButtonWidget _uploadButton;  // our clone
    private TextMeshProUGUI _labelTmp;   // our clone's label
    private Widget _screenshotsParent;
    private float _pollTimer;

    private void Awake()
    {
        Logger.LogInfo($"{PluginName} {PluginVersion} loaded. Injects an Upload Screenshot button into the craft upload dialog.");
    }

    private void Update()
    {
        // Unity's overloaded == reports destroyed objects as null, so this covers the
        // dialog being closed: tear down and start polling for the next one.
        bool live = _dialog != null && _addButton != null && _uploadButton != null && _screenshotsParent != null;
        if (!live)
        {
            if (_dialog != null || _uploadButton != null)
            {
                Reset();
            }
            _pollTimer += Time.unscaledDeltaTime;
            if (_pollTimer >= 0.5f)
            {
                _pollTimer = 0f;
                TryInject();
            }
            return;
        }

        // Keep the label correct (cheap insurance; the cloned TextWidget is not
        // context-initialized, so nothing should revert the raw TMP text).
        if (_labelTmp != null && _labelTmp.text != UploadLabel)
        {
            _labelTmp.text = UploadLabel;
        }

        // Keep our button pinned directly below the add button. The game relocates the
        // add button to the bottom of the list (SetIndex(-1)) whenever the screenshot set
        // changes, so without this our button would drift to a different slot.
        int target = _addButton.transform.GetSiblingIndex() + 1;
        if (_uploadButton.transform.GetSiblingIndex() != target)
        {
            _uploadButton.transform.SetSiblingIndex(target);
        }

        // Mirror the game's 3-screenshot cap. Toggle the GameObject directly: Widget.Visible
        // routes through UpdateVisibility -> Animation.HideAnimation, and Animation is null on
        // our non-context-initialized clone (would NRE when hiding).
        bool shouldShow = CountScreenshots() < MaxScreenshots;
        if (_uploadButton.gameObject.activeSelf != shouldShow)
        {
            _uploadButton.gameObject.SetActive(shouldShow);
        }
    }

    private void TryInject()
    {
        GameObject cloneGo = null;
        try
        {
            UploadDialogScript[] dialogs = UnityEngine.Object.FindObjectsByType<UploadDialogScript>(FindObjectsSortMode.None);
            if (dialogs.Length == 0)
            {
                return;
            }

            UploadDialogScript dlg = dialogs[0];
            Widget root = dlg.Widget;
            if (root == null)
            {
                return; // dialog widget tree not initialized yet
            }

            // FindWidget only walks the registered widget tree (_widgets); our Instantiate'd
            // clone is never registered there, so this can only ever return the native button.
            ButtonWidget addButton = root.FindWidget<ButtonWidget>("add-screenshot-button");
            Widget parent = root.FindWidget("screenshots-parent");
            if (addButton == null || parent == null)
            {
                return;
            }

            // Clone the native button so we inherit the exact game styling.
            cloneGo = UnityEngine.Object.Instantiate(addButton.gameObject, addButton.transform.parent);
            cloneGo.name = CloneName;
            ButtonWidget clone = cloneGo.GetComponent<ButtonWidget>();
            if (clone == null)
            {
                UnityEngine.Object.Destroy(cloneGo);
                return;
            }

            // Kill the inherited Juicy routing (which would re-fire OnAddScreenshotButtonClicked)
            // and drive behaviour off the widget's Clicked event instead.
            clone.EventClick = string.Empty;
            clone.EventHandler = null;
            clone.Clicked += OnUploadClicked;

            // Force the height to match the native button. We can't use Widget.Height here:
            // it dereferences Widget.Rect, which is null on a clone that was never
            // context-initialized. Drive the layout via a LayoutElement + RectTransform instead.
            float height = addButton.Rect != null ? addButton.Rect.rect.height : 0f;
            if (height < 36f)
            {
                height = 50f;
            }
            LayoutElement layout = cloneGo.GetComponent<LayoutElement>() ?? cloneGo.AddComponent<LayoutElement>();
            layout.minHeight = height;
            layout.preferredHeight = height;
            layout.flexibleHeight = 0f;
            RectTransform cloneRect = cloneGo.GetComponent<RectTransform>();
            if (cloneRect != null)
            {
                cloneRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
            }

            // Relabel via the raw TextMeshPro. The cloned child TextWidget is NOT
            // context-initialized (its TextMeshPro field is null), so its Text setter would
            // throw; and since it has no Update, nothing reverts a direct TMP edit.
            _labelTmp = null;
            foreach (TextMeshProUGUI tmp in cloneGo.GetComponentsInChildren<TextMeshProUGUI>(true))
            {
                tmp.text = UploadLabel;
                if (_labelTmp == null)
                {
                    _labelTmp = tmp;
                }
            }

            // Place it directly after the native button (one slot "underneath").
            cloneGo.transform.SetSiblingIndex(addButton.transform.GetSiblingIndex() + 1);

            _dialog = dlg;
            _addButton = addButton;
            _uploadButton = clone;
            _screenshotsParent = parent;
            Logger.LogInfo("Injected Upload Screenshot button into the upload dialog.");
        }
        catch (Exception ex)
        {
            // Never leave a half-built clone behind, or the poll would spawn another every tick.
            if (cloneGo != null)
            {
                UnityEngine.Object.Destroy(cloneGo);
            }
            Reset();
            Logger.LogError("Upload Screenshot button injection failed: " + ex);
        }
    }

    private void OnUploadClicked(Widget widget)
    {
        if (_dialog == null)
        {
            return;
        }
        if (CountScreenshots() >= MaxScreenshots)
        {
            Logger.LogInfo("Screenshot limit (3) reached; not adding.");
            return;
        }

        string path = PickImageFile();
        if (string.IsNullOrEmpty(path))
        {
            return; // user cancelled
        }

        Texture2D texture;
        try
        {
            byte[] bytes = File.ReadAllBytes(path);
            texture = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: false);
            if (!(bool)LoadImageMethod.Invoke(null, new object[] { texture, bytes }))
            {
                Logger.LogWarning("Could not decode image: " + path);
                return;
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning("Failed to read image '" + path + "': " + ex.Message);
            return;
        }

        AddScreenshot(texture);
    }

    // Replicates UploadDialogScript.OnAddScreenshotButtonClicked's success callback.
    private void AddScreenshot(Texture2D texture)
    {
        Widget root = _dialog.Widget;
        ScreenshotListItemScript item = root.Context
            .CreateWidgetFromTemplate("screenshot", _screenshotsParent)
            .GetComponent<ScreenshotListItemScript>();
        item.Texture = texture;
        item.Widget.Height = (float)texture.height / texture.width * _addButton.Rect.sizeDelta.x;

        // Keep the dialog's protected UserScreenshots list and the add-button state in
        // sync via the same private helpers the game calls. Without RefreshUserScreenshotsList
        // the image would not be picked up by OnSubmitRequest.
        Traverse dlg = Traverse.Create(_dialog);
        dlg.Method("RefreshUserScreenshotsList").GetValue();
        dlg.Method("UpdateAddScreenshotButton").GetValue();

        Logger.LogInfo($"Added screenshot {texture.width}x{texture.height} from file to upload.");
    }

    private int CountScreenshots()
    {
        if (_screenshotsParent == null)
        {
            return 0;
        }
        return _screenshotsParent.GetComponentsInChildren<ScreenshotListItemScript>(true).Length;
    }

    private void Reset()
    {
        if (_uploadButton != null)
        {
            _uploadButton.Clicked -= OnUploadClicked;
        }
        _dialog = null;
        _addButton = null;
        _uploadButton = null;
        _labelTmp = null;
        _screenshotsParent = null;
    }

    // Native Win32 file picker on a dedicated STA thread (Ookii is bundled with the game).
    private string PickImageFile()
    {
        string result = null;
        Thread thread = new Thread(delegate ()
        {
            try
            {
                VistaOpenFileDialog dialog = new VistaOpenFileDialog
                {
                    Title = "Select a screenshot to upload",
                    Filter = "Images (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg|All files (*.*)|*.*",
                    Multiselect = false,
                    CheckFileExists = true
                };
                if (dialog.ShowDialog() == WinForms.DialogResult.OK)
                {
                    result = dialog.FileName;
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning("File picker failed: " + ex.Message);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        thread.Join();
        return result;
    }
}
