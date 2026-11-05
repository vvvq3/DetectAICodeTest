const { chromium } = require('playwright');
const path = require('path');
(async () => {
  const dir = process.argv[2];
  const scale = Number(process.argv[3] || 2);
  const names = ['01-eshop-catalog-reviews', '02-bitwarden-folder-cleanup', '03-eshopweb-wishlist-project-style'];
  const b = await chromium.launch();
  const ctx = await b.newContext({ viewport: { width: 1040, height: 1200 }, deviceScaleFactor: scale, colorScheme: 'light' });
  const p = await ctx.newPage();
  for (const n of names) {
    await p.goto('file://' + path.join(dir, n + '.html'));
    // Обрезаем по началу блока «Признаки человека» (или «Файлы»), чтобы картинка была компактной.
    const cut = await p.evaluate(() => {
      const hs = [...document.querySelectorAll('h2')];
      const h = hs.find(x => x.textContent.startsWith('Признаки человека')) || hs.find(x => x.textContent.startsWith('Файлы'));
      return h ? h.getBoundingClientRect().top + window.scrollY - 6 : document.body.scrollHeight;
    });
    await p.screenshot({ path: path.join(dir, n + '.png'), clip: { x: 0, y: 0, width: 1040, height: Math.round(cut) } });
    console.log(n, Math.round(cut));
  }
  await p.goto('file://' + path.join(dir, 'examples.html'));
  await p.emulateMedia({ media: 'print', colorScheme: 'light' });
  await p.pdf({ path: path.join(dir, 'examples.pdf'), format: 'A4', printBackground: true, scale: 0.72, margin: { top: '12mm', bottom: '12mm', left: '10mm', right: '10mm' } });
  await b.close();
})();
