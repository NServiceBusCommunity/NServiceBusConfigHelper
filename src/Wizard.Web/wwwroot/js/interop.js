window.nserviceBusWizard = {
    copyToClipboard: function (text) {
        if (navigator.clipboard && navigator.clipboard.writeText) {
            return navigator.clipboard.writeText(text);
        }

        // Fallback for non-secure contexts where navigator.clipboard is unavailable.
        const area = document.createElement('textarea');
        area.value = text;
        area.style.position = 'fixed';
        area.style.opacity = '0';
        document.body.appendChild(area);
        area.focus();
        area.select();
        try {
            document.execCommand('copy');
        } finally {
            document.body.removeChild(area);
        }
        return Promise.resolve();
    },

    // localStorage, for the answers the wizard remembers between visits. Every access is guarded:
    // private windows and blocked site data make it throw, and the wizard works without it.
    storageGet: function (key) {
        try {
            return window.localStorage.getItem(key);
        } catch {
            return null;
        }
    },

    storageSet: function (key, value) {
        try {
            window.localStorage.setItem(key, value);
        } catch {
        }
    },

    storageRemove: function (key) {
        try {
            window.localStorage.removeItem(key);
        } catch {
        }
    },

    // Saves bytes from .NET (a DotNetStreamReference, which avoids base64 for larger zips) as a file.
    downloadFile: async function (fileName, contentType, streamReference) {
        const buffer = await streamReference.arrayBuffer();
        const blob = new Blob([buffer], {type: contentType});
        const url = URL.createObjectURL(blob);
        const anchor = document.createElement('a');
        anchor.href = url;
        anchor.download = fileName;
        document.body.appendChild(anchor);
        anchor.click();
        document.body.removeChild(anchor);
        URL.revokeObjectURL(url);
    }
};
