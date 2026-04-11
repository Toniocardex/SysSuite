# SysSuite One — Roadmap

## Versione attuale: v1.4.0

---

## ✅ v1.4.0 — Sicurezza Livello 1 (Logging & Anti-Crash) — Completato

- [x] Serilog + Serilog.Sinks.File in `SysSuite.csproj`
- [x] `ConfigureLogging()` in `App.xaml.cs`: file in `%LocalAppData%\SysSuite\Logs\SysSuite_Log.txt` con `RollingInterval.Day` (14 file conservati)
- [x] `Application.UnhandledException`: log `Fatal` con messaggio e stack trace, `Handled = true` dove possibile
- [x] `ContentDialog` amichevole sulla UI via `DispatcherQueue.TryEnqueue` dopo errore non gestito
- [x] `ProcessRunner.RunAsync` / `RunCaptureAsync`: log `Warning` se `ExitCode != 0` (processo e codice)
- [x] Modalità `--boost`: errori loggati e `Log.CloseAndFlush()` prima dell'uscita

---

## ✅ v1.0.0 — Completato

### Build & Compilazione
- Fix MSB3073: MinWidth/MinHeight rimossi da Window (non validi in WinUI 3)
- Fix 7 colori dim/glow con formato hex errato (#RRGGBBAA → #AARRGGBB)
- Fix Wrap="Wrap" su StackPanel (non esiste in WinUI 3)
- Fix stringhe C# interpolate con \n reale invece di escape (5 file)
- Fix "C:\\" non verbatim in DiscoPage.cs
- Fix using SysSuite.Services auto-referenziale in GamingService.cs
- Fix COMPILA_E_FIRMA.bat: CRLF, password, signtool dopo restore NuGet

### UI & Design System
- App.xaml: palette, 5 stili pulsante, card, badge, input, progress bar
- MainWindow.xaml: sidebar brand, nav raggruppata, metriche topbar, status bar
- Tutte le 11 Views implementate (erano placeholder)
- Fix ExtendsContentIntoTitleBar: pulsanti X/min/max non più sovrapposti
- Fix status bar altezza fissa: non si rompe con il badge admin

### Sicurezza & Privilegi
- AdminHelper.cs: admin richiesto solo dove serve
- app.manifest: requireAdministrator → asInvoker (risolve blocco screenshot)
- Badge status bar: Non admin / Amministratore

### Funzionalità
- ServicesManager, FindDuplicates, GetUnusedApps, PingAsync, RestoreNagle
- GetCacheSize, CleanAll browser integrati

### Build & Distribuzione
- SatelliteResourceLanguages: 5 lingue (da 50+)
- DebugType=none: no .pdb in dist
- COMPILA_E_FIRMA.bat riscritto completamente

---

## ✅ v1.1.0 — UI Polish — Completato

- [x] Feedback visivo tab selezionata (Opzione 4 ibrida: XAML resource + code-behind)
      bg verde dim, icona accent, testo bianco, bordo sinistro 2px sull'item attivo
- [x] PrivacyService.DisableStartSuggestions() — toggle suggerimenti Start
- [x] PrivacyService.DisableLockScreenTips() — toggle suggerimenti schermata blocco
- [x] PerformanceService.GetCurrentPlan() — piano energetico corrente visibile
- [x] PerformanceService.RestoreAnimations() — pulsante ripristina animazioni
- [x] RegistryService.Restore() — ripristino backup da file .reg con file picker
- [x] Dialog conferma prima di: Kill processo, DisableStartup, Ripristino registro
- [x] Toast notification Windows native dopo: pulizia completa, Gaming Mode on/off

---

## ✅ v1.2.0 — Killer Features — Completato

- [x] Health Score (0-100) sempre visibile nella topbar
      Calcolato da CPU%, RAM%, spazio disco libero. Verde/ambra/rosso.
- [x] One-Click Boost nella Dashboard
      Pulisce temp, miniature, cache browser, chiude processi non rispondenti.
      Progress bar + contatore MB liberati + toast al completamento.
- [x] Grafico CPU/RAM in MonitorPage
      LiveCharts2 CartesianChart con ultimi 60 punti; aggiornamento ogni 2s.
      Smart merge lista processi + WMI RAM su thread pool (niente freeze UI).

---

## ✅ v1.3.0 — Background & Automazione — Completato

- [x] Toast notification intelligenti (ToastHelper.cs)
      Notifiche Windows native per operazioni completate
- [x] Scheduled Cleanup via Windows Task Scheduler
      Pianificazione giornaliera/settimanale/mensile senza admin
      Mostra stato e prossima esecuzione nell'UI
      Rimozione pianificazione con un click

---

## 💡 Backlog (idee future, non ancora pianificate)

- [ ] Avvisi intelligenti real-time (CPU > 90%, RAM > 85%, disco pieno)
- [ ] Tray icon per girare in background senza taskbar
- [ ] Export lista processi in CSV
- [ ] Filtro/ricerca nella sidebar
- [ ] Impostazioni persistenti (il pulsante ⚙ attuale mostra "prossimamente")
- [ ] Tema chiaro opzionale
