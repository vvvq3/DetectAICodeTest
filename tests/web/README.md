# Проверка веб-версии и GitLab-клиента без настоящего GitLab

- `mock_gitlab.py` — поддельный GitLab API v4. Отдаёт MR из JSON корпуса детектора, так что анализ идёт за секунды и не упирается в лимиты gitlab.com.
- `webtest.js` — браузерный тест страницы (Playwright), 15 проверок: токен и галочка сохраняются, поле ссылки очищается, спиннер, список проверенных MR, перезагрузка вкладки.
- `webauth.js` — проверка входа по паролю (`--password`).

```bash
python3 tests/web/mock_gitlab.py 5098 corpus/ai-default/eshop-catalog-reviews.json corpus/ai-default/bitwarden-folder-cleanup.json &
dotnet .build/aidetect.dll web --port 5091 --no-browser &
node tests/web/webtest.js /tmp          # скриншоты web-busy.png и web-result.png попадут в /tmp
```

MR из поддельного GitLab доступны как `http://127.0.0.1:5098/team/shop/-/merge_requests/N` (N — номер JSON-файла в командной строке). Для `webauth.js` сервер запускать с `--password secret`.
