# Security policy

[Русский](#русский)

## Reporting a vulnerability

Report security problems privately through GitHub's private vulnerability reporting: open this repository's
**Security** tab and choose **Report a vulnerability**
([direct link](https://github.com/Zulut30/Hearthpulse-Companion/security/advisories/new)). Don't open a public issue,
discussion or pull request about it, and don't publish the details until a fixed release is out.

A useful report has:

- the Hearthpulse version (Settings → About) and the operating system;
- what an attacker can do and what they need for it: a position on the network, a crafted file, access to the computer;
- the steps to reproduce it or a proof of concept.

The diagnostics archive (Settings → About → Report a problem) may help. Attach it to the private report only, never to a
public issue when it shows the problem.

We answer in the report's thread and agree on the fix and its release with you. You can be credited in the published
advisory.

## Supported versions

Only the latest release gets security fixes. They ship as a new release through the app's updater, so keep automatic
updates on.

## In scope

- The Hearthpulse app for Windows, macOS and Linux, and its installers from this repository's releases.
- The updater: release signatures, downloads and staged installs.
- What the app keeps and sends: the account session (`account.dat`), settings, crash reports, game records, the stats
  pool client, and what the diagnostics archive cuts out.
- The code in this repository, including the stats pool service (`server/pool`).
- hearthpulse.net and the Hearthpulse data API the app talks to: report them the same way.

## Out of scope

- Hearthstone, Battle.net and other Blizzard services: report those to Blizzard.
- Data from third-party sources (HearthstoneJSON, hsbg.cards and the sources behind Hearthpulse data): report wrong numbers
  as wrong data, not as a vulnerability.
- Attacks that need an already compromised computer or administrator rights on it.
- Social engineering, denial of service by volume, and scanner output without a demonstrated impact.

## Русский

Об уязвимостях сообщайте закрыто, через private vulnerability reporting на GitHub: вкладка **Security** этого
репозитория → **Report a vulnerability**
([прямая ссылка](https://github.com/Zulut30/Hearthpulse-Companion/security/advisories/new)). Не пишите о них в открытых
issue, обсуждениях и pull request и не публикуйте подробности, пока не вышла версия с исправлением.

Укажите версию Hearthpulse (Настройки → О программе) и систему, что может сделать атакующий и что ему для этого нужно,
и как это повторить. Архив диагностики (Настройки → О программе → Сообщить о проблеме) прикладывайте только к закрытому
отчёту.

Исправления выходят только для последней версии, новым выпуском через обновление приложения.

Входит: приложение и установщики из выпусков этого репозитория, обновление, хранение и отправка данных (сессия аккаунта,
настройки, отчёты о сбоях, записи игр, пул статистики, очистка архива диагностики), код этого репозитория (включая
`server/pool`), hearthpulse.net и API данных Hearthpulse.

Не входит: Hearthstone и сервисы Blizzard (о них сообщайте Blizzard), данные сторонних источников (HearthstoneJSON,
hsbg.cards и источники Hearthpulse data — о неверных числах сообщайте как о неверных данных, а не как об уязвимости),
атаки на уже взломанный компьютер или с правами администратора, социальная инженерия, отказ в обслуживании объёмом
запросов и отчёты сканеров без показанного вреда.
