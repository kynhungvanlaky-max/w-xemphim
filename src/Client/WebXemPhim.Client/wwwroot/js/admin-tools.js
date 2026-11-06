window.adminTools = {
    downloadCsv(fileName, content) {
        const withBom = content && content.charCodeAt(0) === 0xFEFF ? content : "\uFEFF" + content;
        this.downloadFile(fileName, "text/csv;charset=utf-8", withBom);
    },
    downloadFile(fileName, mimeType, content) {
        const blob = new Blob([content], { type: mimeType });
        const url = URL.createObjectURL(blob);
        const link = document.createElement("a");
        link.href = url;
        link.download = fileName;
        document.body.appendChild(link);
        link.click();
        link.remove();
        URL.revokeObjectURL(url);
    },
    getStoredJson(key) {
        try {
            return window.localStorage.getItem(key);
        } catch {
            return null;
        }
    },
    setStoredJson(key, value) {
        try {
            window.localStorage.setItem(key, value);
        } catch {
            // Storage can be blocked (private mode / quota); preferences are optional.
        }
    },
    previewImageFromPicker(inputElement) {
        try {
            const file = inputElement && inputElement.files && inputElement.files[0];
            if (!file) {
                return { ok: false, error: "empty" };
            }
            if (!file.type || !file.type.startsWith("image/")) {
                return { ok: false, error: "not_image" };
            }
            return { ok: true, url: URL.createObjectURL(file) };
        } catch {
            return { ok: false, error: "preview_failed" };
        }
    },
    revokeObjectUrl(url) {
        if (!url) return;
        try {
            URL.revokeObjectURL(url);
        } catch {
            // Ignore stale/duplicate revokes.
        }
    },
    registerCommandPalette(dotNetHelper) {
        const handler = (e) => {
            if ((e.ctrlKey || e.metaKey) && (e.key === 'k' || e.key === 'K')) {
                e.preventDefault();
                dotNetHelper.invokeMethodAsync('ToggleCommandPalette');
            }
        };
        window.addEventListener('keydown', handler);
        return {
            dispose: () => window.removeEventListener('keydown', handler)
        };
    }
};
