# 1.4.10.15

Data: 2026-10-09

- Dodano automatyczne usuwanie znanych scamowych obrazków (fałszywe konkursy z MrBeastem i podobne) wraz z ostrzeżeniem dla autora.
- Dodano kanał „always-ban": każdy, kto napisze na nim wiadomość, zostaje zbanowany, a osoby z uprawnieniami moderacyjnymi jedynie wyciszone ... prosił @Laudemort.
- Dodano pożegnalną wiadomość na kanale „always-ban" wyświetlaną po zbanowaniu lub wyciszeniu.
- Dodano komendę do ustawiania kanału „always-ban" oraz komendy do zarządzania listą scamowych obrazków.

## Techniczne

- Scamowe obrazki rozpoznawane są po wyglądzie, więc łapane są także ich zmienione i odnowione wersje, a nie tylko identyczny plik.
- Sprawdzanie obrazków jest pomijane, gdy lista znanych wzorców jest pusta, więc nie obciąża bota.
- Każde rozpoznanie scamowego obrazka trafia do logu, co ułatwia dostrajanie czułości.
- Kanał „always-ban" nie obejmuje botów ani webhooków.
