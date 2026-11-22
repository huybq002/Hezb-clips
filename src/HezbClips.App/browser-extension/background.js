// Hezb Clips - full page capture.
// Uses the Chrome DevTools Protocol (chrome.debugger) so the whole page is rendered in one go, no scrolling.
// The picture is cut into slices (browsers limit the size of one screenshot) and posted to the app on 127.0.0.1.

const DEFAULT_PORT = 47831;
const MAX_CSS_HEIGHT = 40000;   // pages longer than this are cut off
const MAX_CSS_WIDTH = 5000;
const SLICE_PIXELS = 8000;      // tallest slice in device pixels

chrome.action.onClicked.addListener((tab) => capture(tab));

async function badge(text, color, title) {
  await chrome.action.setBadgeBackgroundColor({ color });
  await chrome.action.setBadgeText({ text });
  if (title) await chrome.action.setTitle({ title });
}

async function capture(tab) {
  const { token = '', port = DEFAULT_PORT } = await chrome.storage.local.get(['token', 'port']);
  if (!token) {
    await badge('?', '#E5484D', 'Chưa nhập token. Mở Tùy chọn của tiện ích.');
    chrome.runtime.openOptionsPage();
    return;
  }
  if (!tab || tab.id === undefined || !/^(https?|file):/.test(tab.url || '')) {
    await badge('!', '#E5484D', 'Không chụp được trang này (trang hệ thống của trình duyệt).');
    return;
  }

  const target = { tabId: tab.id };
  const send = (method, params) => chrome.debugger.sendCommand(target, method, params);
  await badge('…', '#4C8DFF', 'Đang chụp cả trang…');
  let attached = false;
  let scrollBack = null; // where the user had scrolled to
  try {
    await chrome.debugger.attach(target, '1.3');
    attached = true;
    await send('Page.enable');

    // load lazy images: walk down the page once, then back to the top
    const walk = await send('Runtime.evaluate', {
      awaitPromise: true, returnByValue: true,
      expression: `(async () => {
        const from = [scrollX, scrollY];
        const full = () => Math.max(document.documentElement.scrollHeight, document.body ? document.body.scrollHeight : 0);
        const step = Math.max(400, innerHeight * 0.8);
        for (let y = 0; y < Math.min(full(), ${MAX_CSS_HEIGHT}); y += step) { scrollTo(0, y); await new Promise(r => setTimeout(r, 120)); }
        scrollTo(0, 0); await new Promise(r => setTimeout(r, 350));
        return from;
      })()`,
    });
    scrollBack = walk && walk.result && walk.result.value;

    const metrics = await send('Page.getLayoutMetrics');
    const size = metrics.cssContentSize || metrics.contentSize;
    const width = Math.max(1, Math.min(Math.ceil(size.width), MAX_CSS_WIDTH));
    const height = Math.max(1, Math.min(Math.ceil(size.height), MAX_CSS_HEIGHT));
    const dprRes = await send('Runtime.evaluate', { expression: 'window.devicePixelRatio', returnByValue: true });
    const scale = Math.min(Math.max(Number(dprRes.result.value) || 1, 1), 2);

    const sliceCss = Math.floor(SLICE_PIXELS / scale);
    const total = Math.ceil(height / sliceCss);
    const session = crypto.randomUUID().replace(/-/g, '');
    const title = encodeURIComponent((tab.title || '').slice(0, 120));

    for (let i = 0; i < total; i++) {
      const y = i * sliceCss;
      const h = Math.min(sliceCss, height - y);
      const shot = await send('Page.captureScreenshot', {
        format: 'png', fromSurface: true, captureBeyondViewport: true,
        clip: { x: 0, y, width, height: h, scale },
      });
      const blob = await (await fetch('data:image/png;base64,' + shot.data)).blob();
      const res = await fetch(`http://127.0.0.1:${port}/part?s=${session}&i=${i}&t=${total}&title=${title}`, {
        method: 'POST', headers: { 'X-Hezb-Token': token, 'Content-Type': 'image/png' }, body: blob,
      });
      if (!res.ok) throw new Error('Hezb Clips từ chối (' + res.status + '): ' + await res.text());
    }
    await badge('✓', '#2EA043', 'Đã gửi về Hezb Clips');
    setTimeout(() => chrome.action.setBadgeText({ text: '' }), 3000);
  } catch (e) {
    const msg = String((e && e.message) || e);
    const hint = /Failed to fetch/i.test(msg)
      ? 'Không kết nối được Hezb Clips. Hãy mở ứng dụng và bật "Chụp cả trang qua trình duyệt".' : msg;
    await badge('!', '#E5484D', hint);
  } finally {
    if (attached) {
      // the walk down the page moved the scroll position: put it back where the user had it
      if (scrollBack) { try { await send('Runtime.evaluate', { expression: `scrollTo(${Number(scrollBack[0]) || 0}, ${Number(scrollBack[1]) || 0})` }); } catch (_) { /* tab closed */ } }
      try { await chrome.debugger.detach(target); } catch (_) { /* tab closed */ }
    }
  }
}
