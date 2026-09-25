// CinemaFlow - Premium Cinema Video Player Controller
(function () {
    window.CinemaPlayer = {
        init: function (containerEl, videoEl) {
            if (!containerEl || !videoEl) return;
            window.CinemaPlayer.dispose(containerEl);

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

            function setBuffering(buffering) {
                clearTimeout(bufferDebounce);
                if (buffering) {
                    bufferDebounce = setTimeout(() => {
                        isBuffering = true;
                        if (bufferingSpinner) bufferingSpinner.classList.add('is-active');
                        containerEl.classList.add('is-buffering');
                    }, 140);
                } else {
                    isBuffering = false;
                    if (bufferingSpinner) bufferingSpinner.classList.remove('is-active');
                    containerEl.classList.remove('is-buffering');
                }
            }

            function updateTime() {
                if (isScrubbing) return;
                const cur = videoEl.currentTime;
                const dur = videoEl.duration || 0;
                const pct = dur > 0 ? (cur / dur) * 100 : 0;
                if (progressTrack) progressTrack.setAttribute('aria-valuenow', Math.round(pct).toString());

                if (progressPlayed) progressPlayed.style.width = pct + '%';
                if (progressHandle) progressHandle.style.left = pct + '%';
                if (timeDisplay) {
                    timeDisplay.textContent = `${formatTime(cur)} / ${formatTime(dur)}`;
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
                if (isPaused) {
                    showControls();
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
            videoEl.addEventListener('error', () => setBuffering(false));

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
            function updateVolumeUI() {
                const muted = videoEl.muted || videoEl.volume === 0;
                const volIcon = containerEl.querySelector('.cp-vol-icon');
                const muteIcon = containerEl.querySelector('.cp-mute-icon');
                if (volIcon) volIcon.style.display = muted ? 'none' : 'block';
                if (muteIcon) muteIcon.style.display = muted ? 'block' : 'none';
                if (volumeSlider) {
                    volumeSlider.value = muted ? 0 : videoEl.volume;
                }
            }

            if (volumeBtn) {
                volumeBtn.addEventListener('click', () => {
                    videoEl.muted = !videoEl.muted;
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
                        containerEl.requestFullscreen();
                    } else if (containerEl.webkitRequestFullscreen) {
                        containerEl.webkitRequestFullscreen();
                    }
                } else {
                    if (document.exitFullscreen) {
                        document.exitFullscreen();
                    }
                }
            }

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
            containerEl._cinemaDispose = () => {
                disposed = true;
                clearTimeout(hideTimeout);
                clearTimeout(bufferDebounce);
                clearTimeout(pulseTimer);
                if (cancelScrub) cancelScrub();
                videoEl.pause();
                videoEl.removeAttribute('src');
                videoEl.load();
            };
        },
        dispose: function (containerEl) {
            if (containerEl && containerEl._cinemaDispose) {
                containerEl._cinemaDispose();
                delete containerEl._cinemaDispose;
            }
        },
        reload: function (videoEl) { videoEl.load(); },
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
