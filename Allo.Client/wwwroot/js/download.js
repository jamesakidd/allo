// Hands the browser a file the app generated on the device. Used for the .ics a task with
// a due date can produce, so adding it to a calendar needs no network and no assumption
// about whose calendar it is.
window.alloDownload = {
    file(name, mimeType, content) {
        const blob = new Blob([content], { type: mimeType });
        const url = URL.createObjectURL(blob);
        const link = document.createElement('a');
        link.href = url;
        link.download = name;
        document.body.appendChild(link);
        link.click();
        link.remove();
        // Revoked on a later tick: revoking immediately can cancel the download on some
        // browsers before they have read the blob.
        setTimeout(() => URL.revokeObjectURL(url), 10000);
    },
};
