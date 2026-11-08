# 1.4.11.7

Data: 2026-11-08

- Naprawiono możliwość przekroczenia limitu wzmocnień karty ultimate przy użyciu wielu pigułek naraz.
- Zmieniono bukiet kwiatów i naszyjnik z diamentem: użyte z podaną liczbą zużywają teraz tylko jedną sztukę, bo i tak działają jeden raz.
- Naprawiono filtrowanie haremu po kilku oznaczeniach: pokazuje karty, które mają wszystkie podane oznaczenia, a wersja z minusem pomija karty z którymkolwiek z nich.
- Naprawiono błąd haremu z wykluczaniem oznaczenia, gdy nie podano jego nazwy.
- Naprawiono ustawianie tła ze stylem w profilu z obrazków o innych proporcjach niż 3:2.
- Naprawiono utratę skalpela lub ramki, gdy nie udało się pobrać obrazka.
- Naprawiono pojedynek testowy z osobą, która nie ma profilu.
- Naprawiono zaniżoną liczbę chętnych zapisaną na kartach postaci z trwałej listy życzeń przy otwieraniu pakietów.
- Naprawiono komunikat przy podmianie oznaczeń, który podawał złą nazwę oznaczenia.
- Naprawiono angielskie nazwy opcji widoczności nakładki w ustawieniach profilu.
- Zmieniono czas odnowienia poleceń, który nie jest już naliczany, gdy polecenie nie zostało wykonane przez błędnie podaną osobę lub argument.
- Naprawiono czarne rogi awatara na obrazku awansu i na zaokrąglonym awatarze w profilu.
- Zmieniono obrazek awansu na większy i ostrzejszy, przez co lepiej wygląda na telefonach.
- Naprawiono profil na stronie, który po zmianach w talii, kartach lub oznaczeniach mógł przez kilkanaście minut pokazywać stare dane.
- Przyspieszono stronę profilu dla osób, które nie mają konta w bocie.
- Naprawiono podwójne wyciszenie, gdy ostrzeżenie dostawał gracz, który już był wyciszony.
- Naprawiono polecenie usuwania wiadomości użytkownika, które często niczego nie usuwało.
- Naprawiono powitanie w prywatnej wiadomości, które nie było wysyłane przy wyłączonym powitaniu na kanale.
- Naprawiono prywatną wiadomość przy banie, która informowała o wyciszeniu zamiast o banie.
- Zmieniono ochronę przed oszukańczymi obrazkami, która nie sprawdza już animacji, aby nikt nie został przypadkiem ukarany.
- Usunięto automatyczne wykrywanie najazdów przy dołączaniu na serwer, bo Discord ma własną ochronę.

## Techniczne

- Dodano ochronę przed zgadywaniem kluczy aplikacji korzystających z bota oraz polecenie do zdejmowania takich blokad.
- Zmieniono logi bota na jednolity format i czytelniejsze raporty, a błędy części poleceń, które wcześniej nie trafiały do logów, są teraz zapisywane.
- Zmniejszono zużycie pamięci przy sprawdzaniu obrazków.
- Dodano automatyczne porządkowanie nieużywanych danych pomocniczych zapamiętanych przez bota.
- Dodano wpis w logach o uruchomieniu bota i połączeniu z Discordem.
