(() => {
  const root = document.getElementById('scanner');
  if (!root) return;
  const video = document.getElementById('camera');
  const canvas = document.getElementById('scan-canvas');
  const context = canvas.getContext('2d', { willReadFrequently: true });
  const status = document.getElementById('scan-status');
  const start = document.getElementById('start-camera');
  const stop = document.getElementById('stop-camera');
  const form = document.getElementById('token-form');
  const input = document.getElementById('qr-token');
  let stream, running = false, busy = false, lastToken = '', lastTime = 0, timer;
  const say = (message, error = false) => { status.textContent = message; status.classList.toggle('scan-error', error); };
  async function submit(token) {
    if (busy || (token === lastToken && Date.now() - lastTime < 5000)) return;
    busy = true; lastToken = token; lastTime = Date.now();
    try {
      const body = new URLSearchParams({ token, __RequestVerificationToken: form.querySelector('[name="__RequestVerificationToken"]').value });
      const response = await fetch(root.dataset.url, { method: 'POST', body, credentials: 'same-origin', headers: { 'Accept': 'application/json' } });
      if (!response.headers.get('content-type')?.includes('application/json')) {
        say('Your session may have expired. Refresh this page and sign in again.', true); stopCamera(); return;
      }
      const result = await response.json();
      say(response.ok ? `${result.name} — ${result.message}. Refresh the list to see the latest attendance.` : result.message || 'Unable to check in.', !response.ok);
      if (response.ok) input.value = '';
    } catch { say('Connection interrupted. Retry the scan; duplicate check-ins are safe.', true); }
    finally { busy = false; }
  }
  function tick() {
    if (!running) return;
    if (!busy && video.readyState >= 2 && video.videoWidth > 0) {
      const scale = Math.min(1, 640 / video.videoWidth);
      canvas.width = Math.round(video.videoWidth * scale); canvas.height = Math.round(video.videoHeight * scale);
      context.drawImage(video, 0, 0, canvas.width, canvas.height);
      const data = context.getImageData(0, 0, canvas.width, canvas.height);
      const qr = window.jsQR(data.data, data.width, data.height, { inversionAttempts: 'dontInvert' });
      if (qr) submit(qr.data);
    }
    timer = window.setTimeout(tick, 180);
  }
  function stopCamera() {
    running = false; clearTimeout(timer); stream?.getTracks().forEach(track => track.stop()); video.srcObject = null;
    start.disabled = false; stop.disabled = true; document.getElementById('camera-placeholder').hidden = false;
  }
  start.addEventListener('click', async () => {
    start.disabled = true;
    try {
      if (!navigator.mediaDevices?.getUserMedia) throw new Error('unavailable');
      stream = await navigator.mediaDevices.getUserMedia({ video: { facingMode: { ideal: 'environment' } }, audio: false });
      video.srcObject = stream; await video.play(); running = true; stop.disabled = false;
      document.getElementById('camera-placeholder').hidden = true; say('Camera ready. Hold a member QR in view.'); tick();
    } catch { stopCamera(); say('Camera unavailable. Allow camera access over HTTPS, or use a USB scanner or manual check-in.', true); }
  });
  stop.addEventListener('click', stopCamera);
  form.addEventListener('submit', e => { e.preventDefault(); submit(input.value.trim()); });
  window.addEventListener('pagehide', stopCamera);
  document.addEventListener('visibilitychange', () => { if (document.hidden) stopCamera(); });
})();
