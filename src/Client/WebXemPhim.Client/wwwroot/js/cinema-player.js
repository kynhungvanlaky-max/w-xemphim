// CinemaFlow - Premium Cinema Video Player Controller
(function () {
    function isHlsStream(url) {
        if (!url || typeof url !== 'string') return false;
        const clean = url.split('?')[0].split('#')[0].toLowerCase();
        return clean.endsWith('.m3u8') || url.includes('.m3u8');
    }

    window.CinemaPlayer = {
        init: function (containerEl, videoEl, dotNetHelper) {
            if (!containerEl || !videoEl) return;
            window.CinemaPlayer.dispose(containerEl);
            containerEl._cinemaDotNet = dotNetHelper;

            const controlsEl = containerEl.querySelector('.cinema-controls');
            const playBtn = containerEl.querySelector('.cp-play-btn');
            const playIcon = containerEl.querySelector('.cp-play-icon');
            const pauseIcon = containerEl.querySelector('.cp-pause-icon');
            const centerPlayPulse = containerEl.querySelector('.cp-center-pulse');
            const bufferingSpinner = containerEl.querySelector('.cp-buffering-spinner');
            const progressTrack = containerEl.querySelector('.cp-progress-track');
            const progressPlayed = containerEl.querySelector('.cp-progress-played');
            const progressBuffered = containerEl.querySelector('.cp-progress-buffered');
            const progressHandle = containerEl.querySelector('.cp-progress-handle');
            const progressTooltip = containerEl.querySelector('.cp-progress-tooltip');
            const timeDisplay = containerEl.querySelector('.cp-time-display');
            const volumeBtn = containerEl.querySelector('.cp-volume-btn');
            const volumeSlider = containerEl.querySelector('.cp-volume-slider');
            const rewindBtn = containerEl.querySelector('.cp-rewind-btn');
            const forwardBtn = containerEl.querySelector('.cp-forward-btn');
            const fullscreenBtn = containerEl.querySelector('.cp-fullscreen-btn');
            const pipBtn = containerEl.querySelector('.cp-pip-btn');

            let isScrubbing = false;
            let hideTimeout = null;
            let cancelScrub = null;
            let isBuffering = false;
            let bufferDebounce = null;
            let pulseTimer = null;
            let disposed = false;
            let lastProgressReportTime = 0;
            let startTime = null;
            let firstFrameReported = false;
            let rebufferCount = 0;

            function formatTime(seconds) {
                if (isNaN(seconds) || seconds < 0) return '00:00';
                const s = Math.floor(seconds % 60);
                const m = Math.floor((seconds / 60) % 60);
                const h = Math.floor(seconds / 3600);
                const pad = (n) => n.toString().padStart(2, '0');
                if (h > 0) {
                    return `${h}:${pad(m)}:${pad(s)}`;
                }
                return `${pad(m)}:${pad(s)}`;
            }

            function sendTelemetry(eventType, value, errorCode) {
                if (!dotNetHelper) return;
                try {
                    dotNetHelper.invokeMethodAsync('OnPlaybackTelemetry', eventType, value, errorCode);
                } catch (e) { }
            }

            function reportStartup() {
                if (firstFrameReported || !startTime) return;
                firstFrameReported = true;
                sendTelemetry('startup', Date.now() - startTime);
            }

            function setBuffering(buffering) {
                clearTimeout(bufferDebounce);
                if (buffering) {
                    bufferDebounce = setTimeout(() => {
                        isBuffering = true;
                        if (bufferingSpinner) {
                            bufferingSpinner.classList.add('is-active');
                            // Chỉ đọc được khi video thực sự đang tải.
                            bufferingSpinner.setAttribute('aria-hidden', 'false');
                        }
                        containerEl.classList.add('is-buffering');
                    }, 140);
                } else {
                    isBuffering = false;
                    if (bufferingSpinner) {
                        bufferingSpinner.classList.remove('is-active');
                        bufferingSpinner.setAttribute('aria-hidden', 'true');
                    }
                    containerEl.classList.remove('is-buffering');
                }
            }

            function updateTime() {
                if (isScrubbing) return;
                const cur = videoEl.currentTime;
                const dur = videoEl.duration || 0;
                const pct = dur > 0 ? (cur / dur) * 100 : 0;
                if (progressTrack) {
                    progressTrack.setAttribute('aria-valuenow', Math.round(pct).toString());
                    // Trình đọc màn hình cần thời gian thật, không phải phần trăm.
                    progressTrack.setAttribute('aria-valuetext', formatTime(cur) + ' / ' + formatTime(dur));
                }

                if (progressPlayed) progressPlayed.style.width = pct + '%';
                if (progressHandle) progressHandle.style.left = pct + '%';
                if (timeDisplay) {
                    timeDisplay.textContent = `${formatTime(cur)} / ${formatTime(dur)}`;
                }

                if (dotNetHelper && !videoEl.paused && Math.abs(cur - lastProgressReportTime) >= 10) {
                    lastProgressReportTime = cur;
                    try {
                        dotNetHelper.invokeMethodAsync('OnPlayerProgress', cur);
                    } catch (e) { }
                }

                // Update buffer
                if (progressBuffered && videoEl.buffered.length > 0) {
                    for (let i = 0; i < videoEl.buffered.length; i++) {
                        if (videoEl.buffered.start(i) <= cur && cur <= videoEl.buffered.end(i)) {
                            const bufPct = (videoEl.buffered.end(i) / dur) * 100;
                            progressBuffered.style.width = bufPct + '%';
                            break;
                        }
                    }
                }
            }

            function triggerPulse(type) {
                if (!centerPlayPulse) return;
                // If currently buffering, skip center pulse so it doesn't clash
                if (isBuffering && (type === 'pulse-play' || type === 'pulse-pause')) return;
                clearTimeout(pulseTimer);
                centerPlayPulse.classList.remove('animate', 'pulse-play', 'pulse-pause', 'pulse-rewind', 'pulse-forward');
                void centerPlayPulse.offsetWidth; // trigger reflow
                centerPlayPulse.classList.add(type, 'animate');
                pulseTimer = setTimeout(() => {
                    centerPlayPulse.classList.remove('animate');
                }, 450);
            }

            function togglePlay() {
                if (videoEl.paused || videoEl.ended) {
                    videoEl.play().catch(() => showControls());
                    triggerPulse('pulse-play');
                } else {
                    videoEl.pause();
                    triggerPulse('pulse-pause');
                }
            }

            function updatePlayState() {
                const isPaused = videoEl.paused;
                containerEl.classList.toggle('is-paused', isPaused);
                if (playIcon) playIcon.style.display = isPaused ? 'block' : 'none';
                if (pauseIcon) pauseIcon.style.display = isPaused ? 'none' : 'block';
                if (playBtn) {
                    // Nhãn của nút phải phản ánh trạng thái thật, nếu không trình đọc màn
                    // hình không biết đang phát hay đang tạm dừng.
                    const label = isPaused ? 'Phát (Phím cách)' : 'Tạm dừng (Phím cách)';
                    playBtn.setAttribute('aria-label', label);
                    playBtn.setAttribute('aria-pressed', isPaused ? 'false' : 'true');
                    playBtn.setAttribute('title', label);
                }
                if (isPaused) {
                    showControls();
                    if (dotNetHelper) {
                        try {
                            dotNetHelper.invokeMethodAsync('OnPlayerPause', videoEl.currentTime || 0);
                        } catch (e) { }
                    }
                } else {
                    resetHideTimer();
                }
            }

            function showControls() {
                containerEl.classList.remove('controls-hidden');
                clearTimeout(hideTimeout);
            }

            function resetHideTimer() {
                if (disposed) return;
                showControls();
                if (!videoEl.paused) {
                    hideTimeout = setTimeout(() => {
                        if (!videoEl.paused && !isScrubbing && !containerEl.contains(document.activeElement)) {
                            containerEl.classList.add('controls-hidden');
                        }
                    }, 2800);
                }
            }

            function seek(e) {
                if (!progressTrack) return;
                const rect = progressTrack.getBoundingClientRect();
                const pos = Math.max(0, Math.min(1, (e.clientX - rect.left) / rect.width));
                const targetTime = pos * (videoEl.duration || 0);
                if (isFinite(targetTime)) {
                    videoEl.currentTime = targetTime;
                    if (progressPlayed) progressPlayed.style.width = (pos * 100) + '%';
                    if (progressHandle) progressHandle.style.left = (pos * 100) + '%';
                }
            }

            // Click video stage to toggle play
            videoEl.addEventListener('click', (e) => {
                e.stopPropagation();
                togglePlay();
            });

            // Double click video stage for quick seek (-10s left, +10s right, fullscreen center)
            videoEl.addEventListener('dblclick', (e) => {
                e.stopPropagation();
                const rect = videoEl.getBoundingClientRect();
                const clickX = e.clientX - rect.left;
                const width = rect.width;
                if (clickX < width * 0.35) {
                    videoEl.currentTime = Math.max(0, videoEl.currentTime - 10);
                    triggerPulse('pulse-rewind');
                } else if (clickX > width * 0.65) {
                    videoEl.currentTime = Math.min(videoEl.duration || 0, videoEl.currentTime + 10);
                    triggerPulse('pulse-forward');
                } else {
                    toggleFullscreen();
                }
            });

            if (playBtn) {
                playBtn.addEventListener('click', (e) => {
                    e.stopPropagation();
                    togglePlay();
                });
            }

            videoEl.addEventListener('play', updatePlayState);
            videoEl.addEventListener('pause', updatePlayState);
            videoEl.addEventListener('timeupdate', updateTime);
            videoEl.addEventListener('loadedmetadata', updateTime);

            // Buffering & loading event listeners
            videoEl.addEventListener('loadstart', () => {
                if (!videoEl.paused) setBuffering(true);
            });
            videoEl.addEventListener('waiting', () => setBuffering(true));
            videoEl.addEventListener('seeking', () => {
                if (videoEl.readyState < 3) setBuffering(true);
            });
            videoEl.addEventListener('playing', () => setBuffering(false));
            videoEl.addEventListener('canplay', () => setBuffering(false));
            videoEl.addEventListener('canplaythrough', () => setBuffering(false));
            videoEl.addEventListener('seeked', () => {
                if (videoEl.readyState >= 3) setBuffering(false);
            });
            videoEl.addEventListener('pause', () => setBuffering(false));
            videoEl.addEventListener('error', () => {
                setBuffering(false);
                // The media error can fire before the Blazor circuit is connected, so the
                // @onerror attribute of the <video> element would be lost. Reporting it over
                // JS interop guarantees the operator sees the error panel instead of a black
                // screen.
                const code = videoEl.error ? videoEl.error.code : 0;
                const message = videoEl.error && videoEl.error.message ? videoEl.error.message : 'MEDIA_ERR_UNKNOWN';
                if (dotNetHelper) {
                    try {
                        dotNetHelper.invokeMethodAsync('OnMediaError', 'MEDIA_ERR_' + code + ' ' + message);
                    } catch (_) {}
                }
            });

            // Progress track interaction
            if (progressTrack) {
                progressTrack.addEventListener('pointerdown', (e) => {
                    e.preventDefault();
                    if (cancelScrub) cancelScrub();
                    isScrubbing = true;
                    seek(e);
                    const onMouseMove = (moveEvent) => seek(moveEvent);
                    const onMouseUp = () => {
                        isScrubbing = false;
                        window.removeEventListener('pointermove', onMouseMove);
                        window.removeEventListener('pointerup', onMouseUp);
                        window.removeEventListener('pointercancel', onMouseUp);
                        cancelScrub = null;
                        updateTime();
                    };
                    cancelScrub = onMouseUp;
                    window.addEventListener('pointermove', onMouseMove);
                    window.addEventListener('pointerup', onMouseUp);
                    window.addEventListener('pointercancel', onMouseUp);
                });
                progressTrack.addEventListener('keydown', (e) => {
                    if (!['ArrowLeft','ArrowRight','Home','End'].includes(e.key)) return;
                    e.preventDefault(); e.stopPropagation();
                    const duration = videoEl.duration || 0;
                    videoEl.currentTime = e.key === 'Home' ? 0 : e.key === 'End' ? duration
                        : Math.max(0, Math.min(duration, videoEl.currentTime + (e.key === 'ArrowRight' ? 10 : -10)));
                });

                progressTrack.addEventListener('mousemove', (e) => {
                    if (!progressTooltip) return;
                    const rect = progressTrack.getBoundingClientRect();
                    const pos = Math.max(0, Math.min(1, (e.clientX - rect.left) / rect.width));
                    const dur = videoEl.duration || 0;
                    progressTooltip.textContent = formatTime(pos * dur);
                    progressTooltip.style.left = (pos * 100) + '%';
                    progressTooltip.style.display = 'block';
                });

                progressTrack.addEventListener('mouseleave', () => {
                    if (progressTooltip) progressTooltip.style.display = 'none';
                });
            }

            // Rewind / Forward
            if (rewindBtn) {
                rewindBtn.addEventListener('click', (e) => {
                    e.stopPropagation();
                    videoEl.currentTime = Math.max(0, videoEl.currentTime - 10);
                    triggerPulse('pulse-rewind');
                    showControls();
                });
            }
            if (forwardBtn) {
                forwardBtn.addEventListener('click', (e) => {
                    e.stopPropagation();
                    videoEl.currentTime = Math.min(videoEl.duration || 0, videoEl.currentTime + 10);
                    triggerPulse('pulse-forward');
                    showControls();
                });
            }

            // Volume
            const VOLUME_STORAGE_KEY = 'cinemaflow.volume';
            let lastNonZeroVolume = 0.6;

            function updateVolumeUI() {
                const muted = videoEl.muted || videoEl.volume === 0;
                const volIcon = containerEl.querySelector('.cp-vol-icon');
                const muteIcon = containerEl.querySelector('.cp-mute-icon');
                if (volIcon) volIcon.style.display = muted ? 'none' : 'block';
                if (muteIcon) muteIcon.style.display = muted ? 'block' : 'none';
                if (volumeSlider) {
                    volumeSlider.value = muted ? 0 : videoEl.volume;
                }
                if (volumeBtn) {
                    const label = muted ? 'Bật tiếng (M)' : 'Tắt tiếng (M)';
                    volumeBtn.setAttribute('aria-label', label);
                    volumeBtn.setAttribute('aria-pressed', muted ? 'true' : 'false');
                    volumeBtn.setAttribute('title', label);
                }
            }

            // Nhớ mức âm lượng giữa các lần mở trang.
            function persistVolume() {
                try {
                    if (videoEl.muted || videoEl.volume === 0) return;
                    lastNonZeroVolume = videoEl.volume;
                    localStorage.setItem(VOLUME_STORAGE_KEY, String(videoEl.volume));
                } catch (_) {}
            }

            function restoreVolume() {
                try {
                    const saved = parseFloat(localStorage.getItem(VOLUME_STORAGE_KEY) || '');
                    if (!isNaN(saved) && saved > 0) {
                        videoEl.volume = saved;
                        lastNonZeroVolume = saved;
                    }
                } catch (_) {}
            }

            restoreVolume();
            videoEl.addEventListener('volumechange', persistVolume);

            if (volumeBtn) {
                volumeBtn.addEventListener('click', () => {
                    if (videoEl.muted || videoEl.volume === 0) {
                        // Bật tiếng phải khôi phục mức âm lượng thật, nếu không nút báo "có
                        // tiếng" trong khi âm lượng vẫn bằng 0.
                        videoEl.muted = false;
                        if (videoEl.volume === 0) videoEl.volume = lastNonZeroVolume || 0.6;
                    } else {
                        videoEl.muted = true;
                    }
                    updateVolumeUI();
                });
            }

            if (volumeSlider) {
                volumeSlider.addEventListener('input', (e) => {
                    videoEl.volume = parseFloat(e.target.value);
                    videoEl.muted = videoEl.volume === 0;
                    updateVolumeUI();
                });
            }

            videoEl.addEventListener('volumechange', updateVolumeUI);

            // Fullscreen
            function toggleFullscreen() {
                if (!document.fullscreenElement) {
                    if (containerEl.requestFullscreen) {
                        // Promise bị từ chối khi trình duyệt không cho phép — nếu bỏ qua,
                        // nút toàn màn hình im lặng không làm gì và console có lỗi chưa xử lý.
                        const request = containerEl.requestFullscreen();
                        if (request && typeof request.catch === 'function') {
                            request.catch(() => { reportUnsupportedFullscreen(); });
                        }
                    } else if (containerEl.webkitRequestFullscreen) {
                        containerEl.webkitRequestFullscreen();
                    } else {
                        // iOS Safari không hỗ trợ toàn màn hình cho khung phim — chỉ hỗ trợ
                        // cho chính thẻ video.
                        if (videoEl.webkitEnterFullscreen) {
                            videoEl.webkitEnterFullscreen();
                        } else {
                            reportUnsupportedFullscreen();
                        }
                    }
                } else {
                    if (document.exitFullscreen) {
                        const exit = document.exitFullscreen();
                        if (exit && typeof exit.catch === 'function') exit.catch(() => {});
                    }
                }
            }

            function reportUnsupportedFullscreen() {
                console.warn('[CinemaPlayer] Fullscreen is not available in this browser.');
                if (dotNetHelper) {
                    try {
                        dotNetHelper.invokeMethodAsync('OnPlayerNotice', 'Trình duyệt này không hỗ trợ toàn màn hình cho khung phim. Hãy xoay ngang máy.');
                    } catch (_) {}
                }
            }

            // Theo dõi trạng thái toàn màn hình để icon, nhãn và bộ đếm ẩn controls đều
            // được cập nhật khi vào/ra toàn màn hình.
            document.addEventListener('fullscreenchange', () => {
                const isFullscreen = !!document.fullscreenElement;
                if (fullscreenBtn) {
                    const label = isFullscreen ? 'Thoát toàn màn hình (F)' : 'Toàn màn hình (F)';
                    fullscreenBtn.setAttribute('aria-label', label);
                    fullscreenBtn.setAttribute('aria-pressed', isFullscreen ? 'true' : 'false');
                    fullscreenBtn.setAttribute('title', label);
                }
                resetHideTimer();
            });

            if (fullscreenBtn) fullscreenBtn.addEventListener('click', toggleFullscreen);

            // PiP
            if (pipBtn) {
                if (document.pictureInPictureEnabled) {
                    pipBtn.addEventListener('click', async () => {
                        try {
                            if (document.pictureInPictureElement) {
                                await document.exitPictureInPicture();
                            } else {
                                await videoEl.requestPictureInPicture();
                            }
                        } catch (err) {
                            console.warn("PiP failed", err);
                        }
                    });
                } else {
                    pipBtn.style.display = 'none';
                }
            }

            // Mouse movement for auto-hiding controls
            containerEl.addEventListener('mousemove', resetHideTimer);
            containerEl.addEventListener('touchstart', resetHideTimer);
            containerEl.addEventListener('mouseleave', () => {
                if (!videoEl.paused && !containerEl.contains(document.activeElement)) containerEl.classList.add('controls-hidden');
            });

            // Keyboard navigation
            containerEl.setAttribute('tabindex', '0');
            containerEl.addEventListener('keydown', (e) => {
                if (['INPUT','TEXTAREA','SELECT','BUTTON'].includes(e.target.tagName)) return;
                switch (e.key) {
                    case ' ':
                    case 'k':
                    case 'K':
                        e.preventDefault();
                        togglePlay();
                        break;
                    case 'f':
                    case 'F':
                        e.preventDefault();
                        toggleFullscreen();
                        break;
                    case 'm':
                    case 'M':
                        e.preventDefault();
                        videoEl.muted = !videoEl.muted;
                        break;
                    case 'ArrowLeft':
                    case 'j':
                    case 'J':
                        e.preventDefault();
                        videoEl.currentTime = Math.max(0, videoEl.currentTime - 10);
                        triggerPulse('pulse-rewind');
                        break;
                    case 'ArrowRight':
                    case 'l':
                    case 'L':
                        e.preventDefault();
                        videoEl.currentTime = Math.min(videoEl.duration || 0, videoEl.currentTime + 10);
                        triggerPulse('pulse-forward');
                        break;
                    case 'ArrowUp':
                        e.preventDefault();
                        videoEl.volume = Math.min(1, videoEl.volume + 0.1);
                        break;
                    case 'ArrowDown':
                        e.preventDefault();
                        videoEl.volume = Math.max(0, videoEl.volume - 0.1);
                        break;
                }
            });

            // Initialize UI
            updatePlayState();
            updateVolumeUI();
            updateTime();
            window.CinemaPlayer.setupWatermark(containerEl);
            containerEl._cinemaDispose = () => {
                disposed = true;
                clearTimeout(hideTimeout);
                clearTimeout(bufferDebounce);
                clearTimeout(pulseTimer);
                if (cancelScrub) cancelScrub();
                if (containerEl._cinemaWatermark) {
                    try { clearInterval(containerEl._cinemaWatermark.interval); } catch (_) {}
                    try { window.removeEventListener('resize', containerEl._cinemaWatermark.onResize); } catch (_) {}
                    containerEl._cinemaWatermark = null;
                }
                if (containerEl._cinemaHls) {
                    try { containerEl._cinemaHls.destroy(); } catch (_) {}
                    containerEl._cinemaHls = null;
                }
                videoEl.pause();
                videoEl.removeAttribute('src');
                videoEl.load();
                containerEl._cinemaDotNet = null;
            };
        },
        dispose: function (containerEl) {
            if (containerEl && containerEl._cinemaDispose) {
                containerEl._cinemaDispose();
                delete containerEl._cinemaDispose;
            }
            if (containerEl && containerEl._cinemaHls) {
                try { containerEl._cinemaHls.destroy(); } catch (_) {}
                delete containerEl._cinemaHls;
            }
            if (containerEl && containerEl._cinemaWatermark) {
                try { clearInterval(containerEl._cinemaWatermark.interval); } catch (_) {}
                try { window.removeEventListener('resize', containerEl._cinemaWatermark.onResize); } catch (_) {}
                delete containerEl._cinemaWatermark;
            }
        },
        /**
         * Vẽ watermark chống chia sẻ trên một canvas phủ lên video.
         * Vẽ bằng canvas khiến việc gỡ watermark bằng DevTools khó hơn nhiều so với một
         * đoạn text trong DOM (chỉ cần xoá node là mất), và vị trí đổi ngẫu nhiên liên tục.
         */
        setupWatermark: function (containerEl) {
            if (!containerEl) return;
            const canvas = containerEl.querySelector('.cp-watermark-canvas');
            if (!canvas) return;
            const text = canvas.getAttribute('data-watermark') || '';
            if (!text) return;
            const ctx = canvas.getContext('2d');
            if (!ctx) return;

            const draw = function () {
                const video = containerEl.querySelector('video');
                const rect = video ? video.getBoundingClientRect() : containerEl.getBoundingClientRect();
                const width = Math.max(1, Math.round(rect.width));
                const height = Math.max(1, Math.round(rect.height));
                const dpr = Math.min(window.devicePixelRatio || 1, 2);

                canvas.width = width * dpr;
                canvas.height = height * dpr;
                canvas.style.width = width + 'px';
                canvas.style.height = height + 'px';
                ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
                ctx.clearRect(0, 0, width, height);

                const fontSize = Math.max(13, Math.min(22, Math.round(width / 34)));
                ctx.font = '600 ' + fontSize + 'px "Be Vietnam Pro", system-ui, -apple-system, sans-serif';
                ctx.textBaseline = 'middle';

                const textWidth = ctx.measureText(text).width;
                const padding = fontSize * 1.2;
                // 6 vùng an toàn, khớp với các class .wm-pos-* của CSS.
                const zone = parseInt(canvas.getAttribute('data-watermark-zone') || '0', 10) || 0;
                const zones = [
                    { x: 0.06, y: 0.08, align: 'left' },
                    { x: 0.94, y: 0.08, align: 'right' },
                    { x: 0.08, y: 0.42, align: 'left' },
                    { x: 0.92, y: 0.42, align: 'right' },
                    { x: 0.06, y: 0.82, align: 'left' },
                    { x: 0.94, y: 0.82, align: 'right' }
                ];
                const spot = zones[Math.min(Math.max(zone, 0), zones.length - 1)];
                const x = spot.align === 'right'
                    ? Math.max(padding, width * spot.x - textWidth)
                    : Math.max(padding, width * spot.x);
                const y = Math.max(padding + fontSize, Math.min(height - padding, height * spot.y));
                const angle = (Math.random() * 0.24) - 0.12;

                ctx.save();
                ctx.translate(x, y);
                ctx.rotate(angle);
                // Bóng đen + chữ trắng mờ: đọc được trên cả nền sáng lẫn nền tối của video.
                ctx.fillStyle = 'rgba(0, 0, 0, 0.38)';
                ctx.fillText(text, 1, 1);
                ctx.fillStyle = 'rgba(255, 255, 255, 0.5)';
                ctx.fillText(text, 0, 0);
                ctx.restore();
            };

            draw();
            const interval = setInterval(draw, 12000);
            const onResize = function () { draw(); };
            window.addEventListener('resize', onResize);
            containerEl._cinemaWatermark = { interval: interval, onResize: onResize };
        },
        isHlsStream: isHlsStream,
        loadSource: function (containerEl, videoEl, streamUrl, autoPlay, resumeSeconds) {
            if (!containerEl || !videoEl) return;
            const dotNetHelper = containerEl._cinemaDotNet;

            if (containerEl._cinemaHls) {
                try { containerEl._cinemaHls.destroy(); } catch (_) {}
                containerEl._cinemaHls = null;
            }

            if (!streamUrl) {
                videoEl.pause();
                videoEl.removeAttribute('src');
                videoEl.load();
                return;
            }

            // Đánh dấu thời điểm bắt đầu tải để đo startup time.
            startTime = Date.now();
            firstFrameReported = false;
            rebufferCount = 0;

            if (isHlsStream(streamUrl)) {
                if (window.Hls && window.Hls.isSupported()) {
                    const hls = new window.Hls({
                        enableWorker: true,
                        backBufferLength: 30,
                        maxBufferLength: 30,
                        maxMaxBufferLength: 60,
                        liveSyncDurationCount: 2,
                        liveMaxLatencyDurationCount: 5,
                        maxLiveSyncPlaybackRate: 1.5,
                        startLevel: -1,
                        capLevelToPlayerSize: true,
                        abrEwmaDefaultEstimate: 800000,
                        abrBandWidthFactor: 0.7,
                        abrBandWidthUpFactor: 0.7,
                        lowLatencyMode: false,
                        debug: false
                    });
                    containerEl._cinemaHls = hls;

                    // Bộ đếm số lần thử lại — không có nó, một lỗi mạng fatal sẽ gọi
                    // startLoad() liên tục và quay vòng lặp busy không dừng.
                    let hlsNetworkRetries = 0;
                    let hlsMediaRetries = 0;

                    hls.attachMedia(videoEl);

                    hls.on(window.Hls.Events.MEDIA_ATTACHED, function () {
                        hls.loadSource(streamUrl);
                    });

                    hls.on(window.Hls.Events.MANIFEST_PARSED, function (event, data) {
                        const rawLevels = data.levels || [];
                        const levels = rawLevels.map((lvl, idx) => ({
                            index: idx,
                            bitrate: lvl.bitrate || 0,
                            width: lvl.width || 0,
                            height: lvl.height || 0,
                            name: lvl.name || (lvl.height ? `${lvl.height}p` : `Level ${idx + 1}`)
                        }));

                        if (dotNetHelper) {
                            try {
                                dotNetHelper.invokeMethodAsync('OnHlsManifestParsed', levels);
                            } catch (_) {}
                        }

                        reportStartup();

                        if (resumeSeconds > 0) {
                            videoEl.currentTime = resumeSeconds;
                        }

                        if (autoPlay) {
                            videoEl.play().catch(() => {});
                        }
                    });

                    hls.on(window.Hls.Events.LEVEL_SWITCHED, function (event, data) {
                        if (dotNetHelper) {
                            try {
                                dotNetHelper.invokeMethodAsync('OnHlsLevelSwitched', data.level);
                            } catch (_) {}
                        }
                    });

                    hls.on(window.Hls.Events.ERROR, function (event, data) {
                        if (!data.fatal) return;
                        if (hls.destroyed) return;

                        // KHÔNG log toàn bộ `data`: hls.js đặt URL manifest/segment (tức là URL
                        // stream đã ký kèm chữ ký) vào data.url — log ra là lộ token trong
                        // console của người xem.
                        const errorType = data.type || 'unknown';
                        const errorDetails = data.details || '';

                        switch (data.type) {
                            case window.Hls.ErrorTypes.NETWORK_ERROR:
                                console.warn('[CinemaPlayer] HLS network error (' + errorDetails + '), retrying...');
                                hlsNetworkRetries += 1;
                                if (hlsNetworkRetries > 3) {
                                    console.error('[CinemaPlayer] HLS network error kept failing, giving up.');
                                    try { hls.destroy(); } catch (_) {}
                                    containerEl._cinemaHls = null;
                                    if (dotNetHelper) {
                                        try {
                                            dotNetHelper.invokeMethodAsync('OnHlsFatalError', 'NETWORK ' + errorDetails);
                                        } catch (_) {}
                                    }
                                    return;
                                }
                                // Backoff để không quay vòng lặp busy khi mạng vẫn lỗi.
                                setTimeout(() => {
                                    if (!hls.destroyed) hls.startLoad();
                                }, Math.min(1000 * Math.pow(2, hlsNetworkRetries - 1), 8000));
                                break;
                            case window.Hls.ErrorTypes.MEDIA_ERROR:
                                console.warn('[CinemaPlayer] HLS media error (' + errorDetails + '), recovering...');
                                hlsMediaRetries += 1;
                                if (hlsMediaRetries > 2) {
                                    console.error('[CinemaPlayer] HLS media error kept failing, giving up.');
                                    try { hls.destroy(); } catch (_) {}
                                    containerEl._cinemaHls = null;
                                    if (dotNetHelper) {
                                        try {
                                            dotNetHelper.invokeMethodAsync('OnHlsFatalError', 'MEDIA ' + errorDetails);
                                        } catch (_) {}
                                    }
                                    return;
                                }
                                hls.recoverMediaError();
                                break;
                            default:
                                console.error('[CinemaPlayer] Unrecoverable HLS fatal error (' + errorDetails + ').');
                                try { hls.destroy(); } catch (_) {}
                                containerEl._cinemaHls = null;
                                if (dotNetHelper) {
                                    try {
                                        dotNetHelper.invokeMethodAsync('OnHlsFatalError', errorDetails || 'HLS fatal error');
                                    } catch (_) {}
                                }
                                break;
                        }
                    });
                } else if (videoEl.canPlayType('application/vnd.apple.mpegurl')) {
                    videoEl.src = streamUrl;
                    const onLoaded = function () {
                        videoEl.removeEventListener('loadedmetadata', onLoaded);
                        if (resumeSeconds > 0) {
                            videoEl.currentTime = resumeSeconds;
                        }
                        if (autoPlay) {
                            videoEl.play().catch(() => {});
                        }
                    };
                    videoEl.addEventListener('loadedmetadata', onLoaded);
                    videoEl.load();
                } else {
                    videoEl.src = streamUrl;
                    videoEl.load();
                }
            } else {
                // Nếu thẻ video đang mang đúng URL này thì không gán lại — tránh tải lại
                // tài nguyên lần thứ hai (Razor đã render src, JS lại set src).
                if (videoEl.getAttribute('src') !== streamUrl) {
                    videoEl.src = streamUrl;
                }
                if (resumeSeconds > 0) {
                    const onLoaded = function () {
                        videoEl.removeEventListener('loadedmetadata', onLoaded);
                        videoEl.currentTime = resumeSeconds;
                        if (autoPlay) {
                            videoEl.play().catch(() => {});
                        }
                    };
                    videoEl.addEventListener('loadedmetadata', onLoaded);
                } else if (autoPlay) {
                    videoEl.play().catch(() => {});
                }
                videoEl.load();
            }
        },
        setQualityLevel: function (containerEl, levelIndex) {
            if (containerEl && containerEl._cinemaHls) {
                containerEl._cinemaHls.currentLevel = levelIndex;
                return true;
            }
            return false;
        },
        // Gọi một Range request rất nhỏ bằng fetch để trình duyệt nhận/gia hạn
        // cookie phiên phát (HttpOnly). Không đổi src của <video>, nên không giật hình.
        refreshPlaybackCookie: async function (streamUrl) {
            if (!streamUrl) return false;
            try {
                const response = await fetch(streamUrl, {
                    method: 'GET',
                    headers: { 'Range': 'bytes=0-0' },
                    credentials: 'include',
                    cache: 'no-store'
                });
                try { await response.body?.cancel(); } catch (_) { /* ignore */ }
                return response.ok || response.status === 206;
            } catch (_) {
                return false;
            }
        },
        reload: function (videoEl) { videoEl.load(); },
        pause: function (videoEl) { if (videoEl && !videoEl.paused) { videoEl.pause(); } },
        cursor: function (videoEl) { return { seconds:videoEl.currentTime || 0, playing:!videoEl.paused && !videoEl.ended }; },
        seekTo: function (videoEl, seconds) { if (Number.isFinite(videoEl.duration)) videoEl.currentTime = Math.max(0, Math.min(seconds, videoEl.duration)); },
        selectShareLink: function () { document.getElementById('watch-share-link')?.select(); },
        focusDialog: function (dialog) {
            dialog.focus();
            dialog.addEventListener('keydown', function (event) {
                if (event.key !== 'Tab') return;
                const targets = Array.from(dialog.querySelectorAll('button:not(:disabled), textarea, input, select, a[href]'));
                if (!targets.length) return;
                const first = targets[0], last = targets[targets.length - 1];
                if (event.shiftKey && (document.activeElement === first || document.activeElement === dialog)) { event.preventDefault(); last.focus(); }
                else if (!event.shiftKey && document.activeElement === last) { event.preventDefault(); first.focus(); }
            });
        }
    };
})();
