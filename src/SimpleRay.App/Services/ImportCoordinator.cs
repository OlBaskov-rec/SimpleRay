using System.IO;
using System.Windows;
using Microsoft.Win32;
using SimpleRay.App.Localization;

namespace SimpleRay.App.Services;

/// <summary>Outcome of an acquisition step (no parsing): a status plus, when Ok, the raw text.</summary>
public enum ImportStatus
{
    Ok,
    Cancelled,               // user dismissed a dialog — do nothing, no status change
    ClipboardReadFail,
    FileReadFail,            // Detail = exception message
    ImageReadFail,           // Detail = exception message
    ImageClipboardReadFail,
    NoImage,
    QrError,                 // Detail = exception message
    QrNotRecognized,
}

public sealed record ImportOutcome(ImportStatus Status, string? Text = null, string? Detail = null);

/// <summary>
/// Owns the WPF-bound acquisition of import text (clipboard / file / QR image / webcam), split
/// out of the view-model. It only acquires and reports a status; parsing/dedup (ProfileImporter)
/// and merging into the list live in the view-model, keeping this class free of profile state.
/// </summary>
public sealed class ImportCoordinator
{
    private static LocalizationManager L => LocalizationManager.Instance;

    public ImportOutcome FromClipboard()
    {
        try
        {
            var text = Clipboard.ContainsText() ? Clipboard.GetText() : "";
            return new ImportOutcome(ImportStatus.Ok, text);
        }
        catch (Exception)
        {
            return new ImportOutcome(ImportStatus.ClipboardReadFail);
        }
    }

    public ImportOutcome FromFile()
    {
        var dlg = new OpenFileDialog
        {
            Title = L["import.file"],
            Filter = "*.conf;*.txt|*.conf;*.txt;*.conf.txt|*.*|*.*",
        };
        if (dlg.ShowDialog() != true)
            return new ImportOutcome(ImportStatus.Cancelled);
        try
        {
            return new ImportOutcome(ImportStatus.Ok, File.ReadAllText(dlg.FileName));
        }
        catch (Exception ex)
        {
            return new ImportOutcome(ImportStatus.FileReadFail, Detail: ex.Message);
        }
    }

    public ImportOutcome FromQrFile()
    {
        var dlg = new OpenFileDialog
        {
            Title = L["import.qrFile"],
            Filter = "*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tiff;*.webp|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tiff;*.webp|*.*|*.*",
        };
        if (dlg.ShowDialog() != true)
            return new ImportOutcome(ImportStatus.Cancelled);

        string? text;
        try
        {
            text = QrDecoder.DecodeFile(dlg.FileName);
        }
        catch (Exception ex)
        {
            return new ImportOutcome(ImportStatus.ImageReadFail, Detail: ex.Message);
        }
        return string.IsNullOrWhiteSpace(text)
            ? new ImportOutcome(ImportStatus.QrNotRecognized)
            : new ImportOutcome(ImportStatus.Ok, text);
    }

    public ImportOutcome FromQrClipboard()
    {
        System.Windows.Media.Imaging.BitmapSource? image;
        try
        {
            image = Clipboard.ContainsImage() ? Clipboard.GetImage() : null;
        }
        catch (Exception)
        {
            return new ImportOutcome(ImportStatus.ImageClipboardReadFail);
        }
        if (image is null)
            return new ImportOutcome(ImportStatus.NoImage);

        string? text;
        try
        {
            text = QrDecoder.Decode(image);
        }
        catch (Exception ex)
        {
            return new ImportOutcome(ImportStatus.QrError, Detail: ex.Message);
        }
        return string.IsNullOrWhiteSpace(text)
            ? new ImportOutcome(ImportStatus.QrNotRecognized)
            : new ImportOutcome(ImportStatus.Ok, text);
    }

    public ImportOutcome FromWebcam()
    {
        var dlg = new WebcamQrWindow { Owner = Application.Current?.MainWindow };
        return dlg.ShowDialog() == true && !string.IsNullOrWhiteSpace(dlg.Result)
            ? new ImportOutcome(ImportStatus.Ok, dlg.Result)
            : new ImportOutcome(ImportStatus.Cancelled);
    }
}
