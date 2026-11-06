using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia.Media.Imaging;

namespace UserIdentityDesktop.ViewModels;

public partial class ChatViewModel
{
    private readonly HashSet<string> clipboardFiles = new();
    private bool clipboardClosed;

    public void AddClipboardImage(Bitmap image)
    {
        if (!CanTypeMessage || VoiceComposerActive || IsSendingMessage || EditingMessage != null) return;
        var path = Path.Combine(Path.GetTempPath(), "FireUser-pasted-" + Guid.NewGuid().ToString("N") + ".png");
        try
        {
            image.Save(path, PngBitmapEncoderOptions.Default);
            clipboardFiles.Add(path);
            AddPendingFiles(new[] { path });
        }
        finally { CleanupClipboardFiles(); }
    }

    private void CleanupClipboardFiles(bool all = false)
    {
        if (IsSendingMessage) return;
        foreach (var path in clipboardFiles.Where(path => all || clipboardClosed || !PendingAttachments.Any(item => item.FilePath == path)).ToArray())
        {
            try { File.Delete(path); clipboardFiles.Remove(path); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
