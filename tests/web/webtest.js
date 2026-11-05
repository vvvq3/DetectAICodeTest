const { chromium } = require('playwright');
(async () => {
  const out = process.argv[2];
  const b = await chromium.launch();
  const ctx = await b.newContext({ viewport: { width: 1100, height: 1000 }, deviceScaleFactor: 2, colorScheme: 'light' });
  const p = await ctx.newPage();
  const base = process.env.AIDETECT_URL || 'http://127.0.0.1:5091/';
  const mr = n => 'http://127.0.0.1:5098/team/shop/-/merge_requests/' + n;
  const ok = (name, cond) => console.log((cond ? 'OK   ' : 'FAIL ') + name);
  await p.goto(base);

  // Токен и галочка вводятся один раз
  await p.fill('#token', 'glpat-test-token');
  await p.dispatchEvent('#token', 'input');
  await p.check('#noArtifacts');

  // Ошибочная ссылка: показывается ошибка, ссылка и токен остаются
  await p.fill('#url', 'https://gitlab.com/group/project/-/issues/1');
  await p.click('#go');
  await p.waitForSelector('.card.warn');
  ok('ошибка показана', (await p.textContent('#result')).includes('Ошибка'));
  ok('после ошибки ссылка осталась для исправления', (await p.inputValue('#url')).includes('issues/1'));
  ok('после ошибки токен на месте', (await p.inputValue('#token')) === 'glpat-test-token');

  // Первый MR
  await p.fill('#url', mr(1));
  await p.click('#go');
  await p.waitForSelector('#busy.on', { timeout: 5000 });
  ok('спиннер появился', await p.isVisible('.spinner'));
  ok('кнопка заблокирована на время анализа', await p.isDisabled('#go'));
  await p.waitForTimeout(1200);
  await p.screenshot({ path: out + '/web-busy.png', clip: { x: 0, y: 0, width: 1100, height: 520 } });
  await p.waitForSelector('#busy', { state: 'hidden', timeout: 120000 });
  ok('отчёт по первому MR появился', (await p.textContent('#result')).includes('Add catalog item reviews'));
  ok('поле ссылки очищено', (await p.inputValue('#url')) === '');
  ok('токен остался', (await p.inputValue('#token')) === 'glpat-test-token');
  ok('галочка осталась', await p.isChecked('#noArtifacts'));

  // Второй MR: новый отчёт заменяет прошлый, оба в списке сессии
  await p.fill('#url', mr(2));
  await p.press('#url', 'Enter');
  await p.waitForSelector('#busy.on', { timeout: 5000 });
  await p.waitForSelector('#busy', { state: 'hidden', timeout: 120000 });
  ok('отчёт по второму MR заменил первый', (await p.textContent('#result')).includes('Folder cleanup') && !(await p.textContent('#result')).includes('Add catalog item reviews'));
  ok('в списке сессии 2 MR', (await p.$$('#hist li')).length === 2);
  ok('токен и галочка снова на месте', (await p.inputValue('#token')) === 'glpat-test-token' && await p.isChecked('#noArtifacts'));
  await p.screenshot({ path: out + '/web-result.png', clip: { x: 0, y: 0, width: 1100, height: 1000 } });

  // Клик по первому MR в списке снова открывает его отчёт
  await p.click('#hist li:nth-child(2)');
  ok('клик по списку открывает прошлый отчёт', (await p.textContent('#result')).includes('Add catalog item reviews'));

  // Перезагрузка вкладки: токен и галочка восстанавливаются
  await p.reload();
  ok('после перезагрузки токен восстановлен', (await p.inputValue('#token')) === 'glpat-test-token');
  ok('после перезагрузки галочка восстановлена', await p.isChecked('#noArtifacts'));
  await b.close();
})().catch(e => { console.error('ERROR', e.message); process.exit(1); });
