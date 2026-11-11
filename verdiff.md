# 1.4.11.17

Data: 2026-11-11

- Przyspieszono większość poleceń kart, m.in. użyj, wyprawę, pojedynek, kartę+, ulepszanie, rynek, czarny rynek, loterię, klatkę, niszczenie, uwalnianie i poświęcanie kart.
- Przyspieszono listę życzeń, sprawdzanie, kto chce kartę, otwieranie pakietów, druciarstwo, listę przedmiotów i codzienne nagrody.
- Przyspieszono wysyłanie długich list w wiadomościach prywatnych.
- Naprawiono karty postaci, których obrazek zmienił się na Shindenie – pokazują teraz aktualny obrazek.
- Zmieniono komunikat przy nieobsługiwanym adresie obrazka – podaje teraz, z jakich stron można brać obrazki.

## Techniczne

- Bot wczytuje z bazy tylko te karty, których dane polecenie potrzebuje, dzięki czemu działa szybciej przy graczach z dużymi kolekcjami.
- Dodano dla strony nowy sposób odświeżania kart postaci z danymi z Shindena, który przy chwilowej niedostępności Shindena prosi o ponowienie.
- Zmieniono zgłaszanie błędów: brakujące obrazki postaci i przerwane przez klienta zapytania do API nie są już zgłaszane jako błędy bota.
