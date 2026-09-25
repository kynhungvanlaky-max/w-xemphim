window.adminTools = {
    downloadCsv(fileName, content) {
        this.downloadFile(fileName, "text/csv;charset=utf-8", "\uFEFF" + content);
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
