const { chromium } = require('playwright');
(async () => {
  const b = await chromium.launch();
  const ctx = await b.newContext({ httpCredentials: { username: 'colleague', password: 'secret' } });
  const p = await ctx.newPage();
  const resp = await p.goto(process.argv[2]);
  console.log('страница:', resp.status());
  await p.fill('#url', 'http://127.0.0.1:5098/team/shop/-/merge_requests/2');
  await p.click('#go');
  await p.waitForSelector('#busy', { state: 'hidden', timeout: 120000 });
  const text = await p.textContent('#result');
  console.log(text.includes('Вероятность') ? 'OK   отчёт получен через пароль' : 'FAIL ' + text.slice(0, 200));
  await b.close();
})().catch(e => { console.error('ERROR', e.message); process.exit(1); });
