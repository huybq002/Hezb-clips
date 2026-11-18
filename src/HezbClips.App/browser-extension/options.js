const $ = (id) => document.getElementById(id);
const msg = (text, ok) => { $('msg').textContent = text; $('msg').className = ok ? 'ok' : 'err'; };

chrome.storage.local.get({ token: '', port: 47831 }).then(({ token, port }) => { $('token').value = token; $('port').value = port; });

$('save').onclick = async () => {
  await chrome.storage.local.set({ token: $('token').value.trim(), port: Number($('port').value) || 47831 });
  msg('Đã lưu.', true);
};

$('test').onclick = async () => {
  try {
    const res = await fetch(`http://127.0.0.1:${Number($('port').value) || 47831}/ping`, { headers: { 'X-Hezb-Token': $('token').value.trim() } });
    if (res.ok) msg('Kết nối được với Hezb Clips.', true);
    else msg(res.status === 401 ? 'Token chưa đúng.' : 'Hezb Clips trả lỗi ' + res.status, false);
  } catch (e) { msg('Không kết nối được. Hãy mở Hezb Clips và bật "Chụp cả trang qua trình duyệt".', false); }
};
