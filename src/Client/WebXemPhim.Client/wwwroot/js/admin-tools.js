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
    },
    _activeR2Uploads: {},
    abortR2Upload(uploadId) {
        if (!uploadId || !this._activeR2Uploads) return;
        const entry = this._activeR2Uploads[uploadId];
        if (entry) {
            entry.aborted = true;
            try { entry.xhr && entry.xhr.abort(); } catch { }
            delete this._activeR2Uploads[uploadId];
        }
    },
    startR2Upload(inputElement, options, dotNetHelper) {
        const uploadId = options && options.uploadId ? String(options.uploadId) : String(Date.now());
        const file = inputElement && inputElement.files && inputElement.files[0];

        const notifyProgress = (pct, stageText) => {
            if (!dotNetHelper) return;
            try {
                dotNetHelper.invokeMethodAsync('OnR2UploadProgress', uploadId, pct, stageText || '');
            } catch { }
        };

        const notifyComplete = (result) => {
            delete this._activeR2Uploads[uploadId];
            if (!dotNetHelper) return;
            try {
                dotNetHelper.invokeMethodAsync('OnR2UploadCompleted', uploadId, result);
            } catch { }
        };

        if (!file) {
            notifyComplete({ ok: false, error: 'Không tìm thấy tệp video đã chọn. Vui lòng chọn lại tệp.' });
            return;
        }

        const entry = { xhr: null, aborted: false };
        this._activeR2Uploads[uploadId] = entry;

        const runServerProxyFallback = (reasonText) => {
            if (entry.aborted) return;
            const fallbackUrl = (options && options.fallbackUploadUrl) || '/api/admin/storage/upload-r2';
            const folder = (options && options.folder) || 'episodes';
            const token = (options && options.authToken) || '';

            notifyProgress(1, reasonText || 'Đang truyền tải video qua máy chủ tới Cloudflare R2...');

            const formData = new FormData();
            formData.append('file', file, file.name);
            formData.append('folder', folder);

            const xhr = new XMLHttpRequest();
            entry.xhr = xhr;
            xhr.open('POST', fallbackUrl, true);
            if (token) {
                xhr.setRequestHeader('Authorization', 'Bearer ' + token);
            }

            let lastPct = -1;
            let lastEmitAt = 0;
            xhr.upload.onprogress = (ev) => {
                if (entry.aborted || !ev.lengthComputable || ev.total <= 0) return;
                const pct = Math.min(94, Math.round((ev.loaded / ev.total) * 94));
                const now = Date.now();
                if (pct !== lastPct && (now - lastEmitAt >= 200 || pct >= 94)) {
                    lastPct = pct;
                    lastEmitAt = now;
                    notifyProgress(pct, 'Đang truyền tải video qua máy chủ tới Cloudflare R2...');
                }
            };

            xhr.upload.onload = () => {
                if (entry.aborted) return;
                notifyProgress(96, 'Máy chủ đã nhận đủ tệp, đang ghi lên Cloudflare R2...');
            };

            xhr.onload = () => {
                if (entry.aborted) return;
                let parsed = null;
                try {
                    parsed = xhr.responseText ? JSON.parse(xhr.responseText) : null;
                } catch { }

                const success = parsed && (parsed.success === true || parsed.Success === true);
                const data = parsed && (parsed.data || parsed.Data);
                const message = parsed && (parsed.message || parsed.Message);

                if (xhr.status >= 200 && xhr.status < 300 && success && data && (data.url || data.Url)) {
                    notifyProgress(100, 'Tải lên Cloudflare R2 thành công!');
                    notifyComplete({
                        ok: true,
                        mode: data.storageMode || data.StorageMode || 'Cloudflare R2',
                        url: data.url || data.Url,
                        playableUrl: data.playableUrl || data.PlayableUrl || data.url || data.Url,
                        objectKey: data.objectKey || data.ObjectKey || '',
                        fileName: file.name,
                        fileSizeBytes: file.size
                    });
                } else {
                    const errMsg = message || ('Máy chủ trả về mã lỗi HTTP ' + xhr.status + ' khi tải lên R2.');
                    notifyComplete({ ok: false, error: errMsg });
                }
            };

            xhr.onerror = () => {
                if (entry.aborted) return;
                notifyComplete({ ok: false, error: 'Kết nối tải lên bị gián đoạn. Vui lòng kiểm tra mạng và thử lại.' });
            };

            xhr.onabort = () => {
                notifyComplete({ ok: false, error: 'Đã hủy quá trình tải tệp lên Cloudflare R2.' });
            };

            xhr.send(formData);
        };

        const presignedUrl = options && options.presignedUrl;
        if (!presignedUrl) {
            runServerProxyFallback('Đang truyền tải video qua máy chủ tới Cloudflare R2...');
            return;
        }

        notifyProgress(1, 'Đang tải trực tiếp từ trình duyệt lên Cloudflare R2...');
        const xhr = new XMLHttpRequest();
        entry.xhr = xhr;
        xhr.open('PUT', presignedUrl, true);
        xhr.setRequestHeader('Content-Type', (options && options.contentType) || 'application/octet-stream');

        let lastPct = -1;
        let lastEmitAt = 0;
        xhr.upload.onprogress = (ev) => {
            if (entry.aborted || !ev.lengthComputable || ev.total <= 0) return;
            const pct = Math.min(98, Math.round((ev.loaded / ev.total) * 98));
            const now = Date.now();
            if (pct !== lastPct && (now - lastEmitAt >= 200 || pct >= 98)) {
                lastPct = pct;
                lastEmitAt = now;
                notifyProgress(pct, 'Đang tải trực tiếp từ trình duyệt lên Cloudflare R2...');
            }
        };

        xhr.onload = () => {
            if (entry.aborted) return;
            if (xhr.status >= 200 && xhr.status < 300) {
                notifyProgress(100, 'Đã tải trực tiếp lên Cloudflare R2 thành công!');
                notifyComplete({
                    ok: true,
                    mode: 'Cloudflare R2 (Direct)',
                    url: (options && options.r2Url) || '',
                    playableUrl: (options && options.playableUrl) || (options && options.r2Url) || '',
                    objectKey: (options && options.objectKey) || '',
                    fileName: file.name,
                    fileSizeBytes: file.size
                });
            } else if (xhr.status === 403 || xhr.status === 400 || xhr.status === 0) {
                runServerProxyFallback('Chuyển sang kênh truyền qua máy chủ tới Cloudflare R2...');
            } else {
                notifyComplete({ ok: false, error: 'Cloudflare R2 từ chối tệp (HTTP ' + xhr.status + ').' });
            }
        };

        xhr.onerror = () => {
            if (entry.aborted) return;
            runServerProxyFallback('R2 chưa bật CORS trực tiếp — đang chuyển sang tải qua máy chủ (không qua SignalR)...');
        };

        xhr.onabort = () => {
            notifyComplete({ ok: false, error: 'Đã hủy quá trình tải tệp lên Cloudflare R2.' });
        };

        xhr.send(file);
    }
};
