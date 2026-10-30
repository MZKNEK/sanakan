# 1.4.10.33

Data: 2026-10-31

- Dodano kod profilu, który zapisuje cały wygląd profilu i pozwala ustawić go jednym poleceniem, po pokazaniu kosztu i potwierdzeniu.
- Dodano możliwość wypisania kodu profilu razem z obrazkami tła, stylu i nakładek, ważnego przez 24 godziny.
- Dodano wczytywanie obrazków z kodu profilu, także tła i stylu z jednego obrazka, z opłatą za każdy obrazek jak przy osobnej zmianie.
- Naprawiono pobieranie opłaty za ponowne ustawienie tego samego stylu lub ramki awatara.

## Techniczne

- Losowanie postaci do kart działa dalej przy chwilowej awarii Shindena i od razu po restarcie bota.
- Bot rzadziej odpytuje Shindena, bo pamięta pobrane dane także między restartami.
