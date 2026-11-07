# 1.4.11.6

Data: 2026-11-08

- Przyspieszono budowanie topki graczy, zwłaszcza rankingów liczby i mocy kart.
- Naprawiono listy kart ultimate i unikatowych na stronie, które po przekazaniu karty mogły jeszcze długo pokazywać poprzedniego właściciela.
- Dodano wyróżnienie automatycznych banów i wyciszeń na kanale powiadomień (osobny kolor i podpis), aby łatwo je odnaleźć i wyszukać.
- Zmieniono zapisywanie usuniętych i edytowanych wiadomości: bot zapamiętuje teraz ostatnie 250 wiadomości na kanale zamiast 600, więc zmiany w starszych wiadomościach nie trafią do logu.

## Techniczne

- Zmniejszono zużycie pamięci przez bota: zmieniono sposób jej zwalniania i ograniczono zapas pamięci trzymany na obróbkę obrazków.
- Dodano limit pamięci na zapamiętane dane z bazy.
- Przyspieszono usuwanie graczy, którzy opuścili serwer, oraz uruchamianie bota, które niepotrzebnie wczytywały wszystkie karty usuniętych graczy.
- Przyspieszono wczytywanie list kart ultimate i unikatowych, które nie są już odświeżane po każdym otwarciu pakietu.
- Dodano godzinny i dobowy raport zużycia pamięci.
- Zmieniono wygląd raportów na kanale logów: są wyróżnione kolorem i nie mieszają się z innymi wpisami.
- Dodano oznaczanie wybranej roli przy błędach w logach, a przy bardzo dużej liczbie błędów najwyżej raz na 30 minut.
