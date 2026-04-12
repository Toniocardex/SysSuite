# SysSuite One — Roadmap

## Versione attuale: v1.5.0

Traccia **v2.1 (Refined)** — prestazioni native e ripristino: sezione dedicata dopo v1.3.0 (completata).

---

## Contesto memorizzato: Dashboard & `HubViewModel` (v1.5.x)

Decisione prodotto: layout **“Real-Time First”** (LiveCharts continui RAM/rete accanto a donut disco/GPU) **non accettato dal cliente**; ripristinato design **a griglia** con card “modulo” e polling sobrio.

### Layout & file
- **`Views/DashboardPage.xaml`**: griglia **riga 1** tre colonne — **Sistema operativo** | **Processore** | **Memoria RAM**; **riga 2** due colonne — **Archiviazione** | **Scheda video**. Sezione **Moduli disponibili** invariata sotto.
- Card: `SysSurfaceBrush`, `SysBorderSubtleBrush`, `CornerRadius="8"`, `Margin="10"`, `Padding="16"` (aspetto modulo rispetto a `SysBGBrush`).
- **`HardwareSnapshotView`** rimosso: contenuto unificato nella Dashboard.
- **`Views/DashboardPage.xaml.cs`**: `ViewModel = GetRequiredService<HubViewModel>()` **prima** di `InitializeComponent()` (richiesto per `x:Bind` verso `ViewModel`).

### Performance & timer (`HubViewModel.cs`)
- **Niente** donut LiveCharts per RAM né polling 1,5 s su metriche disco/RAM come prima iterazione “live”.
- **Disco**: solo card Archiviazione — **`LiveChartsCore` `PieChart`** (donut usato/libero), serie `DiskDonutSeries`; **`DispatcherTimer` 30 s** → `SystemInfo.RefreshDiskVolumeOnly()` + aggiornamento testi/`DiskDonutSeries`/`DiskUsedPercentValue`.
- **GPU**: metriche DXGI via **`GpuMonitorService`**, timer **10 s** (`ApplyGpuSlowSampleToUi`) — testi + `ProgressBar` VRAM, **senza** grafico live aggiuntivo.
- **RAM**: snapshot al load / dopo `LoadDashboardDataAsync` (boost, RAM optimizer), `ProgressBar` lineare; proprietà strutturate CPU: `DashboardCpuName`, `DashboardCpuCoresLine`, `DashboardCpuFreqLine`.

### Binding WinUI
- Dashboard usa **`x:Bind ViewModel.…`** (code-behind = `DashboardPage`); testi popolati dopo `GatherAll` → **`Mode=OneWay`** (non `OneTime` sul primo paint: altrimenti restano `—` / `N/D` finché la pagina non viene ricreata).
- Donut disco: **`{Binding DiskDonutSeries, Mode=OneWay}`** sul `PieChart` (convenzione LiveCharts).

### User delight — brand GPU
- **`GpuBrandBrush`** (`SolidColorBrush`): colore da **`GpuMetrics.Name`** ( DXGI ) — NVIDIA `#76B900`, AMD/Radeon `#ED1C24`, Intel `#0071C5`, default `#3B9EFF`.
- Pennelli **istanze static readonly** condivise; **`TrySetGpuBrandBrushFromAdapterName`** aggiorna solo se cambia il vendor classificato (niente `new SolidColorBrush` ogni 10 s).
- XAML: `Foreground="{x:Bind ViewModel.GpuBrandBrush, Mode=OneWay}"` su icona card GPU e sulla `ProgressBar` VRAM.

### Repository
- Commit di riferimento su `main`: messaggio tipo *«Dashboard: layout griglia, donut disco 30s, GPU brand color e x:Bind»* (rimozione `HardwareSnapshotView`, modifiche sopra).

---

## Contesto memorizzato: Sub-Zero — Rete, potenza, log, storage nativo (v1.5.x+)

Standard **Sub-Zero**: dove richiesto, **`SystemRestoreService.CreateRestorePointAsync`** prima di modifiche invasive al sistema; niente WMI per i nuovi percorsi storage dashboard (solo `DeviceIoControl` / volume IOCTL).

### Network Booster (TCP/IP gaming)
- [x] **`Services/NetworkOptimizationService`**: punto di ripristino *«SysSuite Network Optimization»*; registry `HKLM\…\Tcpip\Parameters\Interfaces` (tutte le sottochiavi: `TcpAckFrequency`, `TCPNoDelay`, `TcpDelAckTicks`); `HKLM\SOFTWARE\Microsoft\MSMQ\Parameters\TCPNoDelay`; `ProcessRunner` → `ipconfig /flushdns`.
- [x] **`NetworkViewModel`**: `OptimizeNetworkCommand`; **`NetworkPage`**: pulsante **Ottimizza**; toast successo (`ToastHelper`).
- [x] DI: `App.xaml.cs` registra `NetworkOptimizationService`.

### Power Management & CPU parking
- [x] **`Services/PowerOptimizationService`**: ripristino *«SysSuite Power Optimization»*; `powercfg -duplicatescheme` / `-setactive` (Ultimate / fallback Prestazioni elevate); registry core parking `ValueMax`/`ValueMin` = 0.
- [x] **`GamingViewModel`**: `OptimizePowerCommand`; card Gaming pulsante **Ottimizza** (tooltip sblocco potenza); toast.
- [x] DI: `PowerOptimizationService` singleton.

### Log: selezione e copia (WinUI)
- [x] **`Core/LogClipboardHelper`**: `DataPackage` + `Clipboard.SetContent`, toast esito.
- [x] **`TextBlock`**: `IsTextSelectionEnabled="True"` sui blocchi log; **`Border.ContextFlyout`** → `MenuFlyoutItem` **Copia log** + `SymbolIcon Copy` su **Gaming**, **Network**, **Browser**, **MainSuite** (log ottimizzazione).

### Dashboard — salute disco nativa (no WMI)
- [x] **`Interop/NtStorage.cs`**: `LibraryImport` `kernel32` (`CreateFileW`, `DeviceIoControl`, `CloseHandle`); `IOCTL_STORAGE_QUERY_PROPERTY`; query temperatura dispositivo/adapter; query NVMe log health (TBW / `%` usura); `IOCTL_VOLUME_GET_VOLUME_DISK_EXTENTS` per mappare il volume di sistema (`C:`) → **`PhysicalDriveN`** (non più fisso `PhysicalDrive0`).
- [x] **`Services/StorageHealthService`**: lettura sul disco fisico risolto; integrazione solo nel **`RefreshDiskVolumeUiAsync`** (stesso timer **30 s** del donut disco).
- [x] **`HubViewModel`**: `DiskTemperature`, `DiskHealth`; **`DashboardPage.xaml`**: righe card Archiviazione (icone termometro / salute).
- [x] DI: `StorageHealthService` singleton.

---

## ✅ Livello 1: Sicurezza (Logging & Exception Handling) — Completato

*(Allineato alla release v1.4.0 — logging strutturato e gestione errori a livello applicazione.)*

- [x] Serilog + Serilog.Sinks.File in `SysSuite.csproj`
- [x] `ConfigureLogging()` in `App.xaml.cs`: file in `%LocalAppData%\SysSuite\Logs\SysSuite_Log.txt` con `RollingInterval.Day` (14 file conservati)
- [x] `Application.UnhandledException`: log `Fatal` con messaggio e stack trace, `Handled = true` dove possibile
- [x] `ContentDialog` amichevole sulla UI via `DispatcherQueue.TryEnqueue` dopo errore non gestito
- [x] `ProcessRunner.RunAsync` / `RunCaptureAsync`: log `Warning` se `ExitCode != 0` (processo e codice)
- [x] Modalità `--boost`: errori loggati e `Log.CloseAndFlush()` prima dell'uscita

---

## ✅ Livello 3: Architettura MVVM & DI — Completato

- [x] **Dependency Injection** (`Microsoft.Extensions.DependencyInjection`): registrazione di servizi e ViewModel in `App.xaml.cs` (`ConfigureServices`)
- [x] **CommunityToolkit.Mvvm**: `ObservableObject`, `[ObservableProperty]`, `[RelayCommand]` per proprietà osservabili e comandi; logica di business nei ViewModel, non nelle viste
- [x] **Architettura asincrona e disaccoppiata**: le viste si limitano a `InitializeComponent()` e assegnazione `DataContext` da `App.Services`; operazioni I/O e registry su thread appropriati tramite i servizi
- [x] **Binding** `{Binding ...}` su gran parte delle schermate WinUI; **`DashboardPage`** usa **`x:Bind`** verso `ViewModel` (vedi sezione *Contesto memorizzato: Dashboard*); `Mode=TwoWay` dove serve (es. Privacy)
- [x] Pagine / moduli portati al pattern MVVM:
  - [x] **BatteryPage** + `BatteryViewModel`
  - [x] **PrivacyPage** (`UserControl` in MainSuite) + `PrivacyViewModel` + `PrivacyService` registrato come singleton
  - [x] **NetworkPage** + `NetworkViewModel`
  - [x] **GamingPage** + `GamingViewModel`
  - [x] **DriverPage** + `DriverViewModel`

> **Nota WinUI:** le `Page` generate dal markup restano con base `Microsoft.UI.Xaml.Controls.Page`; il collegamento al ViewModel avviene nel costruttore code-behind con `GetRequiredService<T>()` (compatibile con il compilatore XAML).

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

## ✅ v2.1 (Refined) — Prestazioni native, servizi & ripristino — Completato

Obiettivo: ridurre overhead managed dove conta (enumerazioni, monitor) e allineare modifiche di sistema a checkpoint di ripristino prima di operazioni invasive.

### Ripristino configurazione (WMI)
- [x] `SystemRestoreService` (`root\default:SystemRestore`): verifica stato ripristino, creazione punto di ripristino asincrona (`Task.Run` + WMI)
- [x] Singleton registrato in DI (`App.xaml.cs`); integrazione prima di **Stop/Disable servizio** e flussi startup sensibili

### Monitor processi (percorso v2.1)
- [x] Enumerazione processi via `NtQuerySystemInformation` (interop nativa), CPU da delta kernel/user, cache percorsi; `ProcessManager` refactor; `AllowUnsafeBlocks` nel progetto

### Avvio con Windows (Startup)
- [x] Lettura voci avvio da registry nativo (`Interop/NtRegistry.cs` + `StartupEntriesService`); scrittura/disabilitazione lato managed dove necessario
- [x] Checkpoint `SystemRestoreService` prima di disabilitare voci startup (registry/file)

### Servizi Windows — enumerazione ultra-veloce
- [x] `Interop/NtServices.cs`: P/Invoke `advapi32` — `OpenSCManagerW`, `EnumServicesStatusExW`, `CloseServiceHandle`
- [x] Enumerazione con livello `SC_ENUM_PROCESS_INFO` (`NtServices.ScEnumProcessInfo`)
- [x] Buffer riusabile `NativeMemory` (fino a 256 KiB); parsing righe con `ReadOnlySpan<byte>` + `MemoryMarshal` su struct SCM
- [x] Tipo avvio da hive `HKLM\SYSTEM\CurrentControlSet\Services` (valore `Start`)
- [x] `ServicesManager`: `EnumerateAllServicesNative()`, cache stato `_lastStateByName` per `GetStatus`; `DisableAsync` / `EnableAsync` / `RestartAsync`
- [x] **Sicurezza:** `CreateRestorePointAsync` (se ripristino attivo) **prima** di stop + `sc config` su disable
- [x] Modello leggero `WindowsServiceListItem`; `WindowsServicesViewModel` + `RefreshAsync` su thread pool

### UI & integrazione
- [x] `MainSuitePage`: `ServicesManager` con `SystemRestoreService` da DI, tab Servizi async (`LoadServicesAsync`), righe `ServiceUiRow`, disable/enable async
- [x] `GamingService`: costruttore con `SystemRestoreService`, `DisableAsync`/`EnableAsync` per servizi Xbox
- [x] **Brand / icone:** `Assets/Brand/` — `AppIcon.ico` (multi-size + `ApplicationIcon` + `AppWindow.SetIcon`), `Logo512.png` (sidebar `MainWindow`), **`Logo256.png`** (viste compatte: intestazione **HubPage**, **SettingsPage**); `Content` con copia in output
- [x] **Monitor — pipeline UI / CPU (contesto v2.1):**
  - [x] **Differential update:** `MonitorViewModel.ApplyDifferentialUpdate` — `Dictionary` PID → `ProcessEntry`, niente `ProcessItems.Clear()`; nuovo PID → `InitializeFromSample` + cache + `ObservableCollection`; uscente → `RemoveAt` + cache; esistente → `ApplyDynamicMetricsFrom` (solo metriche dinamiche).
  - [x] **`BatchingObservableCollection<T>`** (`SysSuite.Collections`): durante `BeginUpdate`/`EndUpdate` niente notifiche intermedie sulla lista; **un solo** `NotifyCollectionChangedAction.Reset` a fine batch **solo** se nel batch ci sono stati **Add o Remove** (coalescenza; Move soppresso senza Reset aggiuntivo, come da regola mandatoria).
  - [x] **Ordinamento fuori thread UI:** `BuildSortedSnapshot` (filtro + sort + top 150) eseguito sul thread pool **prima** di `_dispatcher.TryEnqueue` — la UI riceve già la lista ordinata.
  - [x] **`ProcessEntry`:** `InitializeFromSample` imposta `Name` e `ImagePath` (da `sample.Path`) e il resto alla creazione riga in cache; `ApplyDynamicMetricsFrom` **non** tocca nome/percorso; **CPU:** `PropertyChanged` su `CpuPercent` solo se varia di **≥ 0,2%** rispetto all’ultimo valore notificato (altrimenti aggiornamento silenzioso del campo).
  - [x] **MonitorPage:** niente `ScrollViewer` esterno su tutta la pagina (altezza finita → virtualizzazione); `ListView` con `VirtualizingStackPanel` verticale come `ItemsPanel`.

### Build
- [x] `ServicesManager`: buffer SCM come `nint` + blocchi `unsafe` mirati per `NativeMemory`
- [x] Build soluzione: **0 errori, 0 avvisi** (`dotnet build SysSuite.sln`)

> **Nota performance:** il risparmio su ~400+ servizi non è numerato nel repo; per una cifra reale misurare con `Stopwatch` (enum nativa vs `ServiceController.GetServices()`).

---

## 💡 Backlog (idee future, non ancora pianificate)

- [ ] Avvisi intelligenti real-time (CPU > 90%, RAM > 85%, disco pieno)
- [ ] Tray icon per girare in background senza taskbar
- [ ] Export lista processi in CSV
- [ ] Filtro/ricerca nella sidebar
- [ ] Impostazioni persistenti (il pulsante ⚙ attuale mostra "prossimamente")
- [ ] Tema chiaro opzionale
