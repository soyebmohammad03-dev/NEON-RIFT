using System.Collections.Generic;
using System.Text;
using NeonRift.Game;
using NeonRift.Vehicles;
using UnityEngine;
using UnityEngine.InputSystem;

namespace NeonRift.HarborRain
{
    /// <summary>
    /// Operation KX-7, self-contained (Night Run's director, definitions and results flow are untouched).
    /// GARAGE (pick a car; Level 2 cars unlock here) → arrival reveal (skippable) → 1 enter the port → 2 crack the cargo
    /// manifest (clue puzzle; reveals the bay, launches the rival crew) → 3 reach the restricted area → 4 badge-reader
    /// bypass at the security gate (reel puzzle; loops the cameras) → 5 container lock (twistlocks, seal signal, lift)
    /// → theft alarm: lockdown clock, sirens, storm and flood escalation, drones and patrol pursue, a patrol ROADBLOCK
    /// appears on one approach → 6 reach the east gate (CONTROL reroutes around blockades, floods and the sealed lane
    /// 80 checkpoint along the road graph) → 7 emergency power override (routing puzzle) → 8 the storm-surge coast road
    /// (staged waves; time the crossing or take the dry inland road) → 9 extraction → debrief and Level 2 unlocks.
    /// Optional: spike the drone relay on the berth (drones blind after the alarm) and stay below "suspicious" (ghost).
    /// CONTROL (radio + device) speaks only on real state changes.
    /// </summary>
    public sealed class HarborRainMission : MonoBehaviour
    {
        public enum Phase { Inactive, Garage, Briefing, Enter, Manifest, Breach, Acquire, Escape, Extracted, Failed }
        public enum EscapeStage { ToGate, Override, Coast }

        /// <summary>The operation's objectives in order (the campaign trailer's briefing shows exactly these).</summary>
        public static IReadOnlyList<string> Objectives => Steps;

        private static readonly string[] Steps =
        {
            "ENTER THE PORT", "CRACK THE CARGO MANIFEST", "REACH THE RESTRICTED AREA", "BYPASS THE SECURITY GATE",
            "RELEASE & LIFT KX-7", "ESCAPE TO THE EAST GATE", "OVERRIDE THE GATE POWER", "CROSS THE COAST ROAD", "EXTRACT"
        };

        [Header("Route")]
        [SerializeField] private Transform gatePoint;
        [SerializeField] private Transform manifestPoint;
        [SerializeField] private Transform securityGatePoint;
        [SerializeField] private Transform[] shipmentBays = new Transform[0];
        [SerializeField] private Transform overridePoint;
        [SerializeField] private Transform coastPoint;
        [SerializeField] private Transform relayPoint;
        [SerializeField] private Transform extractionPoint;
        [SerializeField] private Transform[] routeProbes = new Transform[0];
        [SerializeField] private string[] routeNames = new string[0];
        [SerializeField] private Rect restrictedArea;
        [SerializeField] private Rect coastArea;
        [SerializeField] private float reachRadius = 16f;
        [SerializeField] private float interactRadius = 10f;
        [SerializeField] private float interactMaxSpeed = 2.5f;

        [Header("World")]
        [SerializeField] private HarborGate mainGate;
        [SerializeField] private HarborGate securityGate;
        [SerializeField] private HarborGate restrictedNorthGate;
        [SerializeField] private HarborGate extractionGate;
        [SerializeField] private HarborGate laneCheckpoint;
        [SerializeField] private FloodZone[] deepFloods = new FloodZone[0];
        [SerializeField] private FloodZone[] shallowFloods = new FloodZone[0];
        [SerializeField] private StormController storm;
        [SerializeField] private HarborWaves waves;
        [SerializeField] private HarborRadio radio;
        [SerializeField] private HarborCinematics cinematics;
        [SerializeField] private Renderer[] securityLamps = new Renderer[0];
        [SerializeField] private Light[] alarmLights = new Light[0];
        [SerializeField] private Renderer[] shipmentGlows = new Renderer[0];
        [SerializeField] private HarborSecurityCamera[] cameras = new HarborSecurityCamera[0];
        [SerializeField] private HarborDrone[] drones = new HarborDrone[0];
        [SerializeField] private GameObject[] blockades = new GameObject[0];
        [SerializeField] private Rect[] blockadeAreas = new Rect[0];
        [SerializeField] private string[] blockadeNames = new string[0];
        [SerializeField] private ParticleSystem sprayPrefab;
        [SerializeField] private ParticleSystem gateSparks;
        [SerializeField] private Transform[] spreaders = new Transform[0];
        [SerializeField] private Transform[] bayContainers = new Transform[0];
        [SerializeField] private ParticleSystem sealSparks;
        [SerializeField] private ParticleSystem liftRunoff;
        [SerializeField] private AudioClip hydraulicClip, metalImpactClip;
        [Header("Heist set (Harbor Rain 2.0)")]
        [SerializeField] private HarborBayCrane crane;
        [SerializeField] private HarborHeistAlarm heistAlarm;
        [SerializeField] private HarborMuleTruck mule;
        [SerializeField] private HarborSearchlight[] searchlights = new HarborSearchlight[0];
        [SerializeField] private AudioClip craneStrain;
        [Tooltip("Peak crosswind on the exposed coast road (m/s², gusts only).")]
        [SerializeField] private float crosswind = 1.1f;
        [SerializeField] private HarborRainHud hud;
        [SerializeField] private Rect[] mapRoads = new Rect[0];
        [SerializeField] private HarborGarage garage = new();

        [Header("Opposition")]
        [SerializeField] private Vector3[] rivalRoute = new Vector3[0];
        [SerializeField] private Vector3[] rivalEscapeRoute = new Vector3[0];
        [SerializeField] private Vector3[] rivalCoastRoute = new Vector3[0];
        [SerializeField] private Vector3[] patrolLoop = new Vector3[0];
        [Tooltip("The port police (Harbor Rain 2.0). When set it replaces the single scripted patrol car.")]
        [SerializeField] private HarborPolice police;
        [SerializeField] private HarborMusic music;

        [Header("Timing")]
        [SerializeField] private float escapeSeconds = 300f;
        [SerializeField] private float earlyAlarmBonus = 60f;
        [SerializeField] private float lockdownDelay = 2f;
        [SerializeField] private float quayWarningDelay = 15f;
        [SerializeField] private float quayCloseDelay = 40f;
        [SerializeField] private float checkpointCloseDelay = 75f;
        [SerializeField] private float relayBlindSeconds = 45f;

        [Header("Audio")]
        [SerializeField] private AudioSource cues;
        [SerializeField] private AudioSource hackLoop;
        [SerializeField] private AudioSource alarmLoop;
        [SerializeField] private AudioSource sirenLoop;
        [SerializeField] private AudioClip objectiveClip, stepClip, deniedClip, acquiredClip, lockdownClip, warningClip, successClip, failureClip;
        [SerializeField] private AudioClip uiTick, uiConfirm, uiError, uiSuccess, sparkClip, gateClunk, lockRelease, deviceNotify, deviceCritical, sprayClip;
        [SerializeField] private UnityEngine.Audio.AudioMixerGroup sfxGroup;

        public Phase Current { get; private set; }
        public EscapeStage Stage { get; private set; }
        public float EscapeElapsed { get; private set; }
        public float Alert { get; private set; }
        public float MaxAlert { get; private set; }
        public bool AlarmRaised { get; private set; }
        public int TargetBay { get; private set; }
        public string RouteTaken { get; private set; } = "";
        public bool ForceInteract { get; set; }
        public HarborAIDriver Rival { get; private set; }
        public HarborAIDriver Patrol { get; private set; }
        public HarborPolice Police => police;
        public HarborMusic Music => music;
        public int PoliceContacts => patrolHits;
        public HarborSecurityCamera[] Cameras => cameras;
        public HarborDrone[] Drones => drones;
        public HarborGate LaneCheckpoint => laneCheckpoint;
        public HarborGate ExtractionGate => extractionGate;
        public FloodZone[] DeepFloods => deepFloods;
        public HarborWaves Waves => waves;
        public HarborRadio Radio => radio;
        public HarborRoutes Routes => routes;
        public HarborCinematics Cinematics => cinematics;
        public HarborGarage Garage => garage;
        public HarborRainHud Hud => hud;
        public HarborPuzzle ActivePuzzle => puzzle;
        public List<Vector3> RoutePath => route;
        public string RouteBlockedBy { get; private set; }
        public int Reroutes { get; private set; }
        /// <summary>True while the route ahead runs into a wave-swept section: the marker says hold short.</summary>
        public bool HoldShort { get; private set; }
        public int ActiveBlockade { get; private set; } = -1;
        public bool BlockadeRevealed { get; private set; }
        public int PuzzleMistakes { get; private set; }
        public int WaveHits { get; private set; }
        public bool RelaySpiked { get; private set; }
        public string LastGrade { get; private set; }
        public List<VehicleDefinition> LastUnlocks { get; private set; } = new();
        public VehicleController Vehicle => vehicle;
        public NeonRift.Input.NeonRiftControls Controls => context?.Controls;

        public Transform DebugPoint(string name) => name switch
        {
            "gatePoint" => gatePoint,
            "manifestPoint" => manifestPoint,
            "securityGatePoint" => securityGatePoint,
            "shipmentPoint" => shipmentBays[TargetBay],
            "overridePoint" => overridePoint,
            "coastPoint" => coastPoint,
            "relayPoint" => relayPoint,
            _ => extractionPoint
        };

        private GameContext context;
        private VehicleController vehicle;
        private Gameplay.MissionSceneEntry entry;
        private VehicleSpray spray;
        private HarborRoutes routes;
        private readonly List<Vector3> route = new();
        private string routeSignature = "", closureSignature = "";
        private float routeTimer, rerouteFlashUntil;
        private float missionStart, lockdownClock, lockdownTotal, nextDenied, patrolCooldown, penalties, lockoutUntil, blindUntil, interferenceTimer;
        private bool lockdown, quayWarned, quayClosed, checkpointClosed, rivalLaunched, rivalTripped, rivalEscaped, rivalCoasting, wasSpotted, coastAnnounced;
        private bool wasWashed, droneSearchAnnounced;
        private int patrolHits;
        private bool paused, holdingCar;
        private float pausedScale = 1f;
        private HarborPuzzle puzzle;
        private string puzzleKind;
        private System.Random rng = new();
        private MaterialPropertyBlock block;
        private InputAction deviceAction, garageAction;
        private int garageIndex;
        private List<(VehicleDefinition car, bool locked, string requirement)> garageCars;
        private float steerPrev, throttlePrev, brakePrev;
        private Vector3[] spreaderRest = new Vector3[0];
        private Vector3[] containerRest = new Vector3[0];
        private float spreaderDrop, lockTurn, cargoLift, acquireBeat = -1f, spreaderAlign, alignStopped = -1f;
        private int acquireStep;
        private bool acquireFraming;
        private const float AlignOffset = 3.4f, AlignSeconds = 1.8f;
        private bool sealBroken;
        private const float SpreaderTravel = 8.38f, BeatSeconds = 2.6f, LiftedHeight = 8f;
        /// <summary>Metres the KX-7 container has been lifted off its bay.</summary>
        public float CargoLift => crane != null ? crane.BoxLift : cargoLift;
        public HarborBayCrane Crane => crane;
        public HarborMuleTruck Mule => mule;
        public HarborHeistAlarm HeistAlarm => heistAlarm;
        public HarborSearchlight[] Searchlights => searchlights;
        /// <summary>Acquisition stage reached this run: 0 none, 1 positioning, 2 connection, 3 extraction, 4 heist moment, 5 back to driving.</summary>
        public int HeistStage { get; private set; }
        public bool AcquisitionPlaying => acquireBeat >= 0f;
        public Vector3 CrosswindNow { get; private set; }
        private static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");

        // ---------------- Lifecycle ----------------

        public void Begin(GameContext gameContext, Gameplay.MissionSceneEntry missionEntry) => Begin(gameContext, missionEntry, false);

        /// <summary>
        /// <paramref name="campaign"/>: the car was chosen in the campaign's five-car selection, so the level's own garage
        /// is skipped; the car is held until the campaign trailer calls <see cref="StartCampaignOperation"/>.
        /// </summary>
        public void Begin(GameContext gameContext, Gameplay.MissionSceneEntry missionEntry, bool campaign)
        {
            CampaignMode = campaign;
            context = gameContext;
            entry = missionEntry;
            BindVehicle(entry.PlayerVehicle);
            context.Controls.Driving.Interact.performed += OnInteract;
            context.Controls.Driving.Pause.performed += OnPause;
            // Harbor Rain's own extra keys (the shared input asset is not modified).
            deviceAction = new InputAction("HarborDevice", binding: "<Keyboard>/tab");
            deviceAction.AddBinding("<Gamepad>/buttonNorth");
            deviceAction.performed += _ => ToggleDevice();
            deviceAction.Enable();
            garageAction = new InputAction("HarborGarage", binding: "<Keyboard>/g");
            garageAction.AddBinding("<Gamepad>/buttonWest");
            garageAction.performed += _ => { if (paused || Current is Phase.Extracted or Phase.Failed) OpenGarage(); };
            garageAction.Enable();
            routes = new HarborRoutes(mapRoads);
            SpawnOpposition();
            if (hud != null && hud.Minimap != null) hud.Minimap.SetMap(mapRoads, restrictedArea, 260f);
            if (waves != null) waves.Changed += OnWave;
            spreaderRest = System.Array.ConvertAll(spreaders, t => t != null ? t.localPosition : Vector3.zero);
            containerRest = System.Array.ConvertAll(bayContainers, t => t != null ? t.position : Vector3.zero);
            ResetLevel(false, false, false);
            if (music != null) music.Begin();   // once: it loops for the whole session
            if (campaign) { Current = Phase.Inactive; HoldCar(true); return; }
            OpenGarage();
        }

        // ---------------- Campaign ----------------

        public bool CampaignMode { get; private set; }
        /// <summary>Campaign: INTERACT on the debrief continues to the campaign results instead of replaying.</summary>
        public System.Action CampaignContinue { get; set; }
        /// <summary>Campaign: draws the pause menu (the level's own pause card is used otherwise).</summary>
        public System.Action<bool> CampaignPause { get; set; }
        /// <summary>Raised on a successful extraction with the grade and total time.</summary>
        public event System.Action<string, float> Extracted;
        public bool Paused => paused;

        /// <summary>Campaign trailer finished (or skipped): the level resets to its start and the operation begins.</summary>
        public void StartCampaignOperation()
        {
            introPlayed = true;
            holdingCar = true;
            ResetLevel(false, false, true);
        }

        /// <summary>Campaign trailer: the lockdown lights as they look after the theft (reset by <see cref="StartCampaignOperation"/>).</summary>
        public void TrailerLockdown(bool on, float t) => SetLockdownVisuals(on, t);
        /// <summary>Campaign trailer: the target bay's glow.</summary>
        public void TrailerHighlightBay(bool on) => SetShipmentGlows(on ? TargetBay : -1);
        public void ResumeFromPause() => SetPaused(false);
        /// <summary>Campaign trailer: the compound alarm (lamps, floods, bell) as it runs after the theft.</summary>
        public void TrailerAlarm(bool on) { if (heistAlarm == null) return; if (on) heistAlarm.Trigger(); else heistAlarm.ResetAlarm(); }
        /// <summary>Campaign cinematics: hides the level HUD (the cinematic overlay is shown instead).</summary>
        public void SetHudVisible(bool on)
        {
            var doc = hud != null ? hud.GetComponent<UnityEngine.UIElements.UIDocument>() : null;
            if (doc != null && doc.rootVisualElement != null) doc.rootVisualElement.style.display = on ? UnityEngine.UIElements.DisplayStyle.Flex : UnityEngine.UIElements.DisplayStyle.None;
        }

        public Transform GatePoint => gatePoint;
        public Transform ManifestPoint => manifestPoint;
        public Transform SecurityGatePoint => securityGatePoint;
        public Transform[] ShipmentBays => shipmentBays;
        public Transform[] BayContainers => bayContainers;
        public Transform CoastPoint => coastPoint;
        public Transform ExtractionPoint => extractionPoint;


        public void End()
        {
            if (context == null) return;
            context.Controls.Driving.Interact.performed -= OnInteract;
            context.Controls.Driving.Pause.performed -= OnPause;
            deviceAction?.Dispose();
            garageAction?.Dispose();
            if (waves != null) waves.Changed -= OnWave;
            if (paused) SetPaused(false);
            context = null;
        }

        private void OnDestroy() => End();

        private void BindVehicle(VehicleController car)
        {
            vehicle = car;
            spray = vehicle != null ? VehicleSpray.Attach(vehicle, sprayPrefab, sprayClip, sfxGroup) : null;
            if (police != null && vehicle != null) police.SetPlayer(vehicle);
        }

        private void SpawnOpposition()
        {
            var catalog = context.Config.VehicleCatalog;
            if (catalog == null || catalog.Count < 2) return;
            var others = new List<VehicleDefinition>();
            foreach (var v in catalog.Vehicles) if (v != context.Session.SelectedVehicle) others.Add(v);
            if (others.Count < 2) others.AddRange(catalog.Vehicles);
            if (rivalRoute.Length > 1)
                Rival = HarborAIDriver.Create("RivalCrew", others[0], rivalRoute[0], Quaternion.LookRotation(rivalRoute[1] - rivalRoute[0]));
            if (police != null)
            {
                police.Spawn(vehicle, Rival != null && Rival.Vehicle != null ? new[] { Rival.Vehicle } : null);
                police.Event += OnPoliceEvent;
                // The interceptor reads CONTROL's route (the player's likely line); units never park in a wave section,
                // a flood or the roadblock, and slow down through standing water.
                police.RouteAhead = metres => route.Count > 1 ? HarborRoutes.Lookahead(route, metres) : (Vector3?)null;
                police.NoStopping = NoStopping;
                police.WaterDepth = WaterDepthAt;
                foreach (var u in police.Units) VehicleSpray.Attach(u.Car, sprayPrefab, null, sfxGroup);
                if (Rival != null && Rival.Vehicle != null) VehicleSpray.Attach(Rival.Vehicle, sprayPrefab, null, sfxGroup);
            }
            else if (patrolLoop.Length > 1)
                Patrol = HarborAIDriver.Create("SecurityPatrol", others[1], patrolLoop[0], Quaternion.LookRotation(patrolLoop[1] - patrolLoop[0]));
        }

        /// <summary>Back to the start: storm, floods, waves, gates, security, opposition, radio and the car reset in place.</summary>
        public void ResetLevel(bool moveVehicle = true) => ResetLevel(moveVehicle, !introPlayed);

        private bool introPlayed;

        public void ResetLevel(bool moveVehicle, bool playIntro, bool start = true)
        {
            if (paused) SetPaused(false);
            ClosePuzzle(false);
            if (moveVehicle && vehicle != null && entry != null && entry.SpawnPoint != null)
            {
                vehicle.Teleport(entry.SpawnPoint.Position, entry.SpawnPoint.Rotation);
                if (entry.ChaseCamera != null) entry.ChaseCamera.Snap();
            }
            if (storm != null) storm.ResetStorm();
            if (waves != null) waves.ResetWaves();
            if (radio != null) radio.ResetRadio();
            foreach (var f in deepFloods) if (f != null) f.ResetZone();
            foreach (var f in shallowFloods) if (f != null) f.ResetZone();
            foreach (var g in new[] { mainGate, securityGate, restrictedNorthGate, extractionGate, laneCheckpoint }) if (g != null) g.ResetGate();
            foreach (var c in cameras) if (c != null) c.ResetCamera();
            foreach (var d in drones) if (d != null) d.ResetDrone();
            foreach (var b in blockades) if (b != null) b.SetActive(false);
            if (Rival != null) Rival.ResetToStart();
            if (Patrol != null) { Patrol.ResetToStart(); Patrol.Follow(patrolLoop, 11f, true); }
            if (police != null) police.ResetUnits();
            policeCoastCalled = false;
            if (cinematics != null) cinematics.Skip();
            if (entry != null && entry.ChaseCamera != null) entry.ChaseCamera.SetFraming(false, Vector3.zero);
            spreaderDrop = lockTurn = cargoLift = spreaderAlign = 0f;
            alignStopped = -1f;
            acquireStep = 0;
            acquireFraming = false;
            acquireBeat = -1f;
            sealBroken = false;
            ApplyAcquisition(true);
            if (crane != null) crane.ResetCrane();
            if (heistAlarm != null) heistAlarm.ResetAlarm();
            foreach (var sl in searchlights) if (sl != null) sl.Sweep();
            if (mule != null) mule.ResetTruck();
            foreach (var c in cameras) if (c != null) c.React(false, Vector3.zero);
            cranePrepared = muleSent = muleLoaded = false;
            stealAt = -1f;
            HeistStage = 0;

            lockdown = quayWarned = quayClosed = checkpointClosed = rivalLaunched = rivalTripped = rivalEscaped = rivalCoasting = wasSpotted = false;
            coastAnnounced = wasWashed = droneSearchAnnounced = BlockadeRevealed = RelaySpiked = false;
            AlarmRaised = false;
            Alert = MaxAlert = EscapeElapsed = penalties = lockoutUntil = blindUntil = 0f;
            patrolHits = PuzzleMistakes = WaveHits = Reroutes = 0;
            ActiveBlockade = -1;
            RouteTaken = "";
            RouteBlockedBy = null;
            routeSignature = closureSignature = "";
            route.Clear();
            Stage = EscapeStage.ToGate;
            rng = new System.Random();
            TargetBay = shipmentBays.Length > 0 ? rng.Next(shipmentBays.Length) : 0;
            SetShipmentGlows(-1);
            SetLockdownVisuals(false, 0f);
            if (alarmLoop != null) alarmLoop.Stop();
            if (sirenLoop != null) sirenLoop.Stop();
            if (hackLoop != null) hackLoop.Stop();
            if (hud != null)
            {
                hud.ClearWarnings();
                hud.ShowResults(false, null, null);
                hud.ShowHold(false, 0f, null);
                hud.SetTimer(null, false);
                hud.ShowLetterbox(false);
                if (hud.Minimap != null) hud.Minimap.SetRoute(null, false);
            }
            missionStart = Time.time;
            SetPhase(Phase.Briefing);
            if (!playIntro) { if (start) StartOperation(); else Current = Phase.Inactive; return; }
            introPlayed = true;
            if (cinematics != null && cinematics.Play("arrival", () => { if (Current == Phase.Briefing) StartOperation(); }))
            {
                HoldCar(true);
                if (hud != null) { hud.ShowLetterbox(true, true); hud.Banner("HARBOR RAIN", null, 4f); }
                if (radio != null) radio.Say("start");
            }
            else StartOperation();
        }

        private void StartOperation()
        {
            if (hud != null) hud.ShowLetterbox(false);
            HoldCar(false);
            if (music != null) music.Level = 1f;
            if (radio != null) radio.Say("start");
            SetPhase(Phase.Enter);
            Optional();
        }

        // ---------------- Garage ----------------

        public void OpenGarage()
        {
            if (paused) SetPaused(false);
            if (hud != null) hud.ShowResults(false, null, null);
            garageCars = garage.Cars(context.Config.VehicleCatalog);
            garageIndex = 0;
            for (int i = 0; i < garageCars.Count; i++) if (garageCars[i].car == context.Session.SelectedVehicle) garageIndex = i;
            Current = Phase.Garage;
            if (music != null) music.Level = 0.6f;
            HoldCar(true);
            RefreshGarage();
        }

        private void RefreshGarage()
        {
            if (hud == null || garageCars == null || garageCars.Count == 0) return;
            var (car, locked, requirement) = garageCars[garageIndex];
            var body = new StringBuilder();
            body.AppendLine($"{car.Manufacturer.ToUpperInvariant()}   ·   {car.Category?.ToUpperInvariant()}   ·   {car.DrivetrainLabel}");
            body.AppendLine();
            body.AppendLine(locked ? $"LOCKED  —  {requirement}" : car.Description);
            body.AppendLine();
            for (int i = 0; i < garageCars.Count; i++)
                body.Append(i == garageIndex ? "  ◆ " : "  ◇ ").Append(garageCars[i].locked ? "🔒" : "");
            body.AppendLine();
            body.AppendLine();
            body.Append(locked ? "◀ ▶  CHOOSE" : "◀ ▶  CHOOSE      E  DRIVE");
            hud.ShowGarage(true, $"HARBOR RAIN GARAGE  ·  {car.DisplayName.ToUpperInvariant()}", body.ToString());
        }

        /// <summary>Validation / touch: pick a garage entry by index and confirm it.</summary>
        public bool ChooseCar(int index)
        {
            if (Current != Phase.Garage || garageCars == null || index < 0 || index >= garageCars.Count) return false;
            garageIndex = index;
            return ConfirmGarage();
        }

        public bool ConfirmGarage()
        {
            if (Current != Phase.Garage) return false;
            var (car, locked, _) = garageCars[garageIndex];
            if (locked) { Cue(deniedClip); return false; }
            if (vehicle == null || context.Session.SelectedVehicle != car) SwapCar(car);
            PlayerPrefs.SetString(HarborGarage.LastCarKey, car.Id);
            PlayerPrefs.Save();
            if (hud != null) hud.ShowGarage(false);
            Cue(uiConfirm);
            Current = Phase.Inactive;
            holdingCar = false;
            ResetLevel(true, !introPlayed);
            return true;
        }

        public IReadOnlyList<(VehicleDefinition car, bool locked, string requirement)> GarageCars => garageCars;

        private void SwapCar(VehicleDefinition car)
        {
            context.Session.SelectVehicle(car, context.Config.VehicleCatalog);
            var old = entry.PlayerVehicle;
            entry.Exit();
            if (old != null) { old.gameObject.SetActive(false); Destroy(old.gameObject); }
            entry.Enter(context);
            BindVehicle(entry.PlayerVehicle);
            holdingCar = false;
            HoldCar(true);
            Debug.Log($"[HarborRain] car → {car.Id}");
        }

        private void UpdateGarage(PuzzleInput input)
        {
            if (garageCars == null || garageCars.Count == 0) return;
            if (input.Left || input.Up) { garageIndex = (garageIndex + garageCars.Count - 1) % garageCars.Count; Cue(uiTick); RefreshGarage(); }
            if (input.Right || input.Down) { garageIndex = (garageIndex + 1) % garageCars.Count; Cue(uiTick); RefreshGarage(); }
        }

        // ---------------- Flow ----------------

        private void SetPhase(Phase phase)
        {
            Current = phase;
            Debug.Log($"[HarborRain] phase {phase}");
            if (hud == null) return;
            switch (phase)
            {
                case Phase.Enter:
                    Objective("ENTER THE PORT", "Pass the south security perimeter into Kowloon Bay container port. Your forged pass opens the checkpoint.");
                    break;
                case Phase.Manifest:
                    Objective("CRACK THE CARGO MANIFEST", "The terminal office by the inspection checkpoint holds the manifest. Park at the kiosk and press INTERACT.");
                    Cue(objectiveClip);
                    break;
                case Phase.Breach:
                    Objective("REACH THE RESTRICTED AREA", $"KX-7 is in bay {BayName(TargetBay)} inside the restricted cargo area. Stay out of camera cones and drone searchlights.");
                    Cue(objectiveClip);
                    break;
                case Phase.Acquire:
                    Objective("RELEASE & LIFT KX-7", $"Bay {BayName(TargetBay)}. Park beside the container and press INTERACT: twistlocks, seal signal, lift.");
                    Cue(stepClip);
                    break;
                case Phase.Escape:
                    Objective("ESCAPE TO THE EAST GATE", "Follow CONTROL's route. Floods, the lane 80 checkpoint and patrol roadblocks will close roads.");
                    hud.Banner("SHIPMENT ACQUIRED", new Color(1f, 0.35f, 0.85f));
                    Cue(acquiredClip);
                    break;
                case Phase.Extracted:
                    Objective("EXTRACTED", "Shipment KX-7 delivered.");
                    hud.SetTimer(null, false);
                    if (alarmLoop != null) alarmLoop.Stop();
                    if (sirenLoop != null) sirenLoop.Stop();
                    if (police != null) police.StandDown();
                    if (music != null) music.Level = 0.75f;
                    Cue(successClip);
                    break;
                case Phase.Failed:
                    hud.SetTimer(null, false);
                    hud.ShowResults(true, "LOCKDOWN COMPLETE", "Port security sealed the terminal before you got out.\n\nE  RETRY      G  GARAGE");
                    if (alarmLoop != null) alarmLoop.Stop();
                    if (police != null) police.StandDown();
                    if (music != null) music.Level = 0.75f;
                    Cue(failureClip);
                    if (radio != null) radio.Say("failed");
                    break;
            }
        }

        private int StepIndex() => Current switch
        {
            Phase.Enter => 0, Phase.Manifest => 1, Phase.Breach => 2, Phase.Acquire => 4,
            Phase.Escape => Stage == EscapeStage.ToGate ? 5 : Stage == EscapeStage.Override ? 6 : 7,
            _ => Steps.Length
        };

        private void Objective(string title, string detail)
        {
            int step = StepIndex();
            hud.SetObjective($"HARBOR RAIN  ·  OPERATION KX-7  ·  {Mathf.Min(step + 1, Steps.Length)} / {Steps.Length}", title, detail);
            hud.SetSteps(Steps, step);
        }

        private void Optional()
        {
            if (hud == null) return;
            bool ghost = MaxAlert < 0.6f && !AlarmRaised;
            hud.SetOptional($"OPTIONAL\n{(RelaySpiked ? "■" : "□")}  SPIKE THE DRONE RELAY (BERTH APRON)\n{(Current >= Phase.Escape ? (ghost ? "■" : "✕") : ghost ? "□" : "✕")}  GHOST: STAY BELOW SUSPICIOUS UNTIL THE THEFT\n{(Stage == EscapeStage.Coast || Current == Phase.Extracted ? (WaveHits == 0 ? "□" : "✕") : "□")}  STORM RUNNER: NO WAVE HITS ON THE COAST ROAD");
        }

        private void Update()
        {
            if (context == null) return;
            var input = ReadPuzzleInput();
            if (Current == Phase.Garage) { UpdateGarage(input); return; }
            if (vehicle == null || Current == Phase.Inactive) return;
            Vector3 p = vehicle.Body != null ? vehicle.Body.position : vehicle.transform.position;
            float dt = Time.deltaTime;
            float speed = vehicle.Body != null ? vehicle.Body.linearVelocity.magnitude : 0f;

            if (puzzle != null) UpdatePuzzle(input, dt);
            UpdateFloods(p, dt);
            if (waves != null && storm != null) waves.Intensity = storm.Intensity;
            if (Current is Phase.Enter or Phase.Manifest or Phase.Breach or Phase.Acquire) UpdateSecurity(p, speed, dt);
            UpdateRadioAmbience(dt);

            Transform target = null;
            string markerText = null;
            switch (Current)
            {
                case Phase.Briefing:
                    return;
                case Phase.Enter:
                    target = gatePoint; markerText = "SECURITY PERIMETER";
                    if (mainGate != null && !mainGate.IsOpen && Flat(p, gatePoint) < 50f)
                    {
                        mainGate.SetOpen(true);
                        Warn("PERIMETER: FORGED PASS ACCEPTED", false, 4f);
                        if (entry.ChaseCamera != null) entry.ChaseCamera.Focus(gatePoint.position + Vector3.up * 4f, 2.5f, 0.25f);
                    }
                    if (Flat(p, gatePoint) < reachRadius)
                    {
                        SetPhase(Phase.Manifest);
                        Say("inside");
                        Say("optional_relay");
                    }
                    break;
                case Phase.Manifest:
                    target = manifestPoint; markerText = "MANIFEST TERMINAL";
                    Station(p, speed, manifestPoint, "MANIFEST TERMINAL");
                    if (securityGatePoint != null && Flat(p, securityGatePoint) < 25f) Denied("RESTRICTED AREA LOCKED — CRACK THE MANIFEST FIRST");
                    break;
                case Phase.Breach:
                    target = securityGatePoint; markerText = "SECURITY GATE";
                    if (hud != null) hud.SetSteps(Steps, Flat(p, securityGatePoint) < interactRadius * 4f ? 3 : 2);
                    Station(p, speed, securityGatePoint, Time.time < lockoutUntil ? $"READER LOCKED  {lockoutUntil - Time.time:0.0}s" : "BADGE READER");
                    break;
                case Phase.Acquire:
                    target = shipmentBays[TargetBay]; markerText = "KX-7";
                    Station(p, speed, shipmentBays[TargetBay], "CONTAINER KX-7");
                    for (int i = 0; i < shipmentBays.Length; i++)
                        if (i != TargetBay && Flat(p, shipmentBays[i]) < interactRadius && context.Controls.Driving.Interact.WasPressedThisFrame())
                        {
                            Denied($"WRONG CONTAINER — KX-7 IS IN BAY {BayName(TargetBay)}");
                            PuzzleMistakes++;
                        }
                    break;
                case Phase.Escape:
                    UpdateEscape(p, speed, dt, out target, out markerText);
                    break;
            }
            // Optional relay spike (before the theft).
            if (!RelaySpiked && relayPoint != null && Current is Phase.Manifest or Phase.Breach or Phase.Acquire && puzzle == null &&
                Flat(p, relayPoint) < interactRadius * 3f)
                Station(p, speed, relayPoint, "DRONE RELAY (OPTIONAL)", "relay");

            UpdateOpposition(p, dt);
            UpdateRoute(p, target, dt);
            UpdateHud(p, target, markerText);
            UpdateAcquisition(dt);
        }

        private void FixedUpdate()
        {
            if (vehicle == null || vehicle.Body == null) return;
            foreach (var f in deepFloods) if (f != null) f.ApplyDrag(vehicle.Body, Time.fixedDeltaTime);
            foreach (var f in shallowFloods) if (f != null) f.ApplyDrag(vehicle.Body, Time.fixedDeltaTime);
            if (waves != null) waves.ApplyForces(vehicle, Time.fixedDeltaTime);
            // Crosswind on the exposed coast road: gusts only, capped, eased off at low speed; always along the storm's
            // wind direction so it reads as weather, not a random shove.
            CrosswindNow = Vector3.zero;
            if (storm != null && Current == Phase.Escape && coastArea.Contains(new Vector2(vehicle.Body.position.x, vehicle.Body.position.z)))
            {
                var w = storm.WindDirection;
                float speed = vehicle.Body.linearVelocity.magnitude;
                CrosswindNow = new Vector3(w.x, 0f, w.z) * (crosswind * storm.Gust * storm.Intensity * Mathf.InverseLerp(3f, 25f, speed));
                vehicle.Body.AddForce(CrosswindNow, ForceMode.Acceleration);
            }
        }

        // ---------------- Interactions and puzzles ----------------

        private PuzzleInput ReadPuzzleInput()
        {
            var d = context.Controls.Driving;
            float steer = d.Steer.ReadValue<float>(), throttle = d.Throttle.ReadValue<float>(), brake = d.Brake.ReadValue<float>();
            var input = new PuzzleInput
            {
                Steer = steer,
                Left = steer < -0.5f && steerPrev >= -0.5f,
                Right = steer > 0.5f && steerPrev <= 0.5f,
                Up = throttle > 0.5f && throttlePrev <= 0.5f,
                Down = brake > 0.5f && brakePrev <= 0.5f,
                Confirm = d.Interact.WasPressedThisFrame(),
                ConfirmHeld = d.Interact.IsPressed() || ForceInteract,
                Cancel = d.Handbrake.WasPressedThisFrame()
            };
            steerPrev = steer; throttlePrev = throttle; brakePrev = brake;
            return input;
        }

        /// <summary>A parked interaction point: shows range / speed prompts; INTERACT opens its puzzle.</summary>
        private void Station(Vector3 p, float speed, Transform point, string name, string kind = null)
        {
            if (puzzle != null || hud == null || acquireBeat >= 0f) { if (acquireBeat >= 0f && hud != null) hud.ShowHold(false, 0f, null); return; }
            float d = Flat(p, point);
            bool near = d < interactRadius * 4f;
            if (!near) { hud.ShowHold(false, 0f, null); return; }
            string label = d >= interactRadius ? $"{name}  ·  MOVE CLOSER  ({d:0} M)" : speed >= interactMaxSpeed ? $"{name}  ·  STOP THE VEHICLE" :
                           Time.time < lockoutUntil && kind == null ? $"{name}  ·  LOCKED OUT {lockoutUntil - Time.time:0.0}s" : $"{name}  ·  PRESS  E";
            hud.ShowHold(true, d < interactRadius && speed < interactMaxSpeed ? 1f : 0f, label);
            bool press = context.Controls.Driving.Interact.WasPressedThisFrame() || ForceInteract;
            if (press && d < interactRadius && speed < interactMaxSpeed && Time.time >= lockoutUntil) OpenPuzzle(kind ?? Current.ToString());
        }

        /// <summary>Opens the station's puzzle (also the validation entry point).</summary>
        public bool OpenPuzzle(string kind)
        {
            if (puzzle != null || Time.time < lockoutUntil && kind == "Breach") return false;
            puzzleKind = kind;
            switch (kind)
            {
                case "Manifest": puzzle = new ManifestPuzzle(rng, TargetBay); Say("manifest_open"); break;
                case "Breach":
                    var reels = new ReelPuzzle(rng);
                    puzzle = reels;
                    Say("gate_open");
                    if (hud != null) hud.DeviceCard("CONTROL", $"BADGE CODE  {Symbols(reels.Target)}  — lock the reels in this order.");
                    Cue(deviceNotify);
                    break;
                case "Acquire": puzzle = new LockPuzzle(rng); Say("lock_start"); break;
                case "Override": puzzle = new PowerPuzzle(rng); break;
                case "relay": puzzle = new LockPuzzle(rng, true, 3f); break;
                default: return false;
            }
            puzzle.Cue += OnPuzzleCue;
            HoldCar(true);
            if (hud != null) { hud.ShowHold(false, 0f, null); hud.ShowPuzzle(puzzle); }
            if (hackLoop != null) hackLoop.Play();
            return true;
        }

        private static string Symbols(int[] code)
        {
            var names = new[] { "▲", "■", "●", "◆", "✚", "✕" };
            var sb = new StringBuilder();
            foreach (int c in code) sb.Append(names[c % 6]).Append(' ');
            return sb.ToString().Trim();
        }

        private void OnPuzzleCue(string cue)
        {
            switch (cue)
            {
                case "tick": Cue(uiTick, 0.5f); break;
                case "confirm": Cue(puzzleKind == "Acquire" ? lockRelease : uiConfirm); break;
                case "error":
                    Cue(uiError);
                    PuzzleMistakes++;
                    if (puzzleKind == "Manifest") { Alert = Mathf.Min(0.95f, Alert + 0.15f); Say("manifest_wrong"); }
                    if (puzzleKind == "Acquire") Say("lock_fail");
                    break;
                case "success": Cue(uiSuccess); break;
                case "spark": Cue(sparkClip); if (gateSparks != null && puzzleKind == "Override") gateSparks.Emit(60); break;
            }
        }

        private void UpdatePuzzle(PuzzleInput input, float dt)
        {
            if (input.Cancel) { ClosePuzzle(true); return; }
            puzzle.Tick(dt, input);
            if (puzzle.Status == HarborPuzzle.State.Running) return;
            var done = puzzle;
            string kind = puzzleKind;
            ClosePuzzle(false);
            if (done.Status == HarborPuzzle.State.Solved) PuzzleSolved(kind, done);
            else PuzzleFailed(kind);
        }

        private void ClosePuzzle(bool cancelled)
        {
            if (puzzle == null) return;
            puzzle.Cue -= OnPuzzleCue;
            puzzle = null;
            if (hud != null) hud.ShowPuzzle(null);
            if (hackLoop != null) hackLoop.Stop();
            HoldCar(false);
            if (cancelled) Warn("STEPPED AWAY — PRESS E AT THE STATION TO RESUME", false, 3f);
            if (cancelled && crane != null && acquireBeat < 0f) { crane.Abort(); acquireStep = 0; }
        }

        private void PuzzleSolved(string kind, HarborPuzzle done)
        {
            switch (kind)
            {
                case "Manifest":
                    TargetBay = ((ManifestPuzzle)done).AnswerBay;
                    SetShipmentGlows(TargetBay);
                    Warn($"KX-7 LOCATED: RESTRICTED AREA, BAY {BayName(TargetBay)}", false, 6f);
                    Say($"manifest_r{TargetBay + 1}");
                    if (hud != null) hud.DeviceCard("OVERWATCH", $"DRONE IMAGE: KX-7 · BAY {BayName(TargetBay)} · blue twistlock caps.");
                    LaunchRival();
                    SetPhase(Phase.Breach);
                    break;
                case "Breach":
                    if (securityGate != null) securityGate.SetOpen(true);
                    Cue(gateClunk);
                    foreach (var c in cameras) if (c != null && c.Restricted) c.SetOffline(true);
                    Warn("RESTRICTED CAMERAS LOOPED — DRONES STILL ACTIVE", false, 6f);
                    Say("gate_ok");
                    if (entry.ChaseCamera != null) entry.ChaseCamera.Focus(securityGate != null ? securityGate.transform.position + Vector3.up * 3f : securityGatePoint.position, 2f, 0.35f);
                    SetPhase(Phase.Acquire);
                    break;
                case "Acquire":
                    // The lift: hold the car for a short beat while the crane hoists KX-7 off the bay, then the alarm.
                    acquireBeat = 0f;
                    beatWait = 0f;
                    HoldCar(true);
                    Cue(hydraulicClip);
                    Cue(metalImpactClip, 0.8f);
                    if (hud != null) hud.Banner("KX-7 LIFTING", new Color(1f, 0.75f, 0.3f), 2.4f);
                    if (entry.ChaseCamera != null && TargetBay < bayContainers.Length && bayContainers[TargetBay] != null)
                        entry.ChaseCamera.CinematicBeat(bayContainers[TargetBay].position + Vector3.up * 7f, BeatSeconds + 0.8f);
                    break;
                case "Override":
                    if (extractionGate != null) extractionGate.SetOpen(true);
                    Cue(gateClunk);
                    Say("override_ok");
                    Stage = EscapeStage.Coast;
                    if (waves != null) waves.Begin();
                    if (entry.ChaseCamera != null) entry.ChaseCamera.Focus(extractionGate.transform.position + Vector3.up * 4f, 2f, 0.3f);
                    Objective("CROSS THE COAST ROAD", "Storm surge on the sea road. Watch the wave beacons and cross between sets, or take the dry inland service road.");
                    if (Rival != null && rivalCoastRoute.Length > 1 && !rivalEscaped) { Rival.Follow(rivalCoastRoute, 20f, false); rivalCoasting = true; }
                    routeTimer = 0f;
                    break;
                case "relay":
                    RelaySpiked = true;
                    Say("relay_done");
                    Cue(uiSuccess);
                    Optional();
                    break;
            }
        }

        private void PuzzleFailed(string kind)
        {
            switch (kind)
            {
                case "Breach":
                    lockoutUntil = Time.time + 4f;
                    Alert = Mathf.Min(0.95f, Alert + 0.2f);
                    Say("gate_fail");
                    Warn("BADGE READER TRIPPED — LOCKED OUT 4 S, SUSPICION RISING", true, 4f);
                    break;
                case "Override":
                    penalties += 5f;
                    Say("override_fail");
                    Warn("BREAKER TRIPPED — LOCKDOWN −5 S. TRY AGAIN", true, 4f);
                    break;
                case "relay":
                    Warn("RELAY SPIKE FAILED — TRY AGAIN", false, 3f);
                    break;
            }
        }

        /// <summary>Holds the car on its brakes (puzzles, garage, cinematics) and gives control back afterwards.</summary>
        private void HoldCar(bool hold)
        {
            if (vehicle == null || holdingCar == hold) { holdingCar = hold && vehicle != null; return; }
            holdingCar = hold;
            IVehicleInputSource source = hold ? new ParkedInput(vehicle) : new NeonRift.Input.PlayerDrivingInput(context.Controls);
            foreach (var r in vehicle.GetComponentsInChildren<IVehicleInputReceiver>()) r.SetInputSource(source);
        }

        public bool CarHeld => holdingCar;

        private sealed class ParkedInput : IVehicleInputSource
        {
            private readonly VehicleController car;
            public ParkedInput(VehicleController c) => car = c;
            // Brake to a stop, then handbrake only (holding brake at rest selects reverse).
            public DrivingInput ReadInput() => new() { Brake = car != null && car.Body != null && car.Body.linearVelocity.magnitude > 0.6f ? 1f : 0f, Handbrake = true };
        }

        private void Denied(string message)
        {
            if (Time.time < nextDenied) return;
            nextDenied = Time.time + 5f;
            Warn(message, true, 4f);
            Cue(deniedClip);
        }

        // ---------------- Cargo acquisition ----------------

        private bool cranePrepared, muleSent, muleLoaded;
        private float beatWait;
        private float stealAt = -1f;
        private const float StealLift = 4.2f;

        private void UpdateAcquisition(float dt)
        {
            if (crane == null) { UpdateAcquisitionLegacy(dt); return; }
            var lp = puzzleKind == "Acquire" ? puzzle as LockPuzzle : null;
            var box = TargetBay < bayContainers.Length ? bayContainers[TargetBay] : null;
            Vector3 p = vehicle != null && vehicle.Body != null ? vehicle.Body.position : Vector3.zero;

            // Stage A, positioning: as the car nears the bay, CONTROL drives the gantry over KX-7 (spreader high, work
            // lights on the box), the looped cameras swing toward the job, and the chase camera glances at the set.
            if (Current == Phase.Acquire && !cranePrepared && box != null && Flat(p, box.position) < 130f)
            {
                cranePrepared = true;
                HeistStage = Mathf.Max(HeistStage, 1);
                crane.Prepare(TargetBay);
                foreach (var c in cameras) if (c != null && c.Restricted) c.React(true, box.position);
                Warn("CONTROL HAS RMG 07 — GANTRY MOVING OVER KX-7", false, 4f);
                Say("crane_align");
                if (entry.ChaseCamera != null) entry.ChaseCamera.Focus(box.position + Vector3.up * 9f, 2.6f, 0.22f);
            }

            if (acquireBeat >= 0f)
            {
                // Stage C, the lift beat: the hoist takes KX-7 off the stack (any seating still running is hurried first).
                HeistStage = Mathf.Max(HeistStage, 3);
                crane.Hoist(LiftedHeight);
                beatWait += dt;
                if (beatWait > 10f && crane.Current is not (HarborBayCrane.Step.Hoisting or HarborBayCrane.Step.Locked or HarborBayCrane.Step.Lifting)) crane.CompleteSeating();
                if (crane.Current is HarborBayCrane.Step.Hoisting) acquireBeat += dt;
                if (crane.BoxLift >= StealLift || acquireBeat >= BeatSeconds + 1.6f)
                {
                    acquireBeat = -1f;
                    HoldCar(false);
                    Steal();
                    HeistStage = 5;
                }
            }
            else if (lp != null)
            {
                // Stage B, connection: square up, flippers down, lower onto the castings, the locks turn one by one.
                HeistStage = Mathf.Max(HeistStage, 2);
                if (crane.Current is HarborBayCrane.Step.Parked or HarborBayCrane.Step.Positioning or HarborBayCrane.Step.Ready or HarborBayCrane.Step.Returning) crane.Connect();
                if (acquireStep == 0) { acquireStep = 1; Cue(hydraulicClip, 0.55f); Warn("SPREADER ALIGNING OVER KX-7", false, 2.5f); }
                if (acquireStep == 1 && crane.Current == HarborBayCrane.Step.Descending) { acquireStep = 2; Warn("ALIGNED — SPREADER LOWERING", false, 2.5f); }
                if (acquireStep == 2 && crane.Seated) { acquireStep = 3; Warn("SPREADER SEATED — ENGAGE THE TWISTLOCKS", false, 3f); }
                if (lp.Stage >= 1) crane.Lock();
                if (acquireStep == 3 && crane.LocksEngaged >= 4) { acquireStep = 4; Warn("TWISTLOCKS LOCKED — 4/4", false, 3f); Say("crane_locked"); }
                if (lp.Stage >= 2 && !sealBroken)
                {
                    sealBroken = true;
                    Cue(sparkClip);
                    Cue(metalImpactClip, 0.6f);
                    Warn("SEAL BROKEN — TAKE UP THE LOAD", false, 3f);
                    if (sealSparks != null && box != null)
                    {
                        sealSparks.transform.position = box.position + new Vector3(0f, 1.2f, -6.2f);
                        sealSparks.Emit(80);
                    }
                }
                if (lp.Stage >= 2) crane.SetLift(lp.LiftProgress * 1.2f);
            }

            // While the lock puzzle runs the chase camera holds a wide, high framing of the car, the box and the crane.
            bool frame = lp != null && lp.Status == HarborPuzzle.State.Running && box != null;
            if (frame != acquireFraming && entry != null && entry.ChaseCamera != null)
            {
                acquireFraming = frame;
                entry.ChaseCamera.SetFraming(frame, frame ? box.position + Vector3.up * 6f : Vector3.zero, 32f, 5f);
            }
            // Rainwater streaming off the box as it comes up (heaviest just off the stack).
            if (liftRunoff != null)
            {
                var e = liftRunoff.emission;
                bool lifting = crane.Carrying && crane.BoxLift > 0.3f && box != null;
                e.rateOverTime = lifting ? Mathf.Lerp(150f, 30f, Mathf.Clamp01(crane.BoxLift / 9f)) : 0f;
                if (lifting) liftRunoff.transform.SetPositionAndRotation(box.position + Vector3.up * 0.05f, box.rotation * Quaternion.Euler(0f, 90f, 0f));
            }

            // Stage D/E after the theft: searchlights hand over from the box to the car; the box goes onto the crew
            // trailer; the truck leaves by the north gate once the car is well clear.
            if (stealAt >= 0f)
            {
                if (Time.time - stealAt > 3.2f && vehicle != null)
                    foreach (var sl in searchlights) if (sl != null && sl.Tracking) sl.Track(vehicle.transform, p);
                if (!muleLoaded && mule != null && crane.Deliveries > 0)
                {
                    muleLoaded = true;
                    mule.Load(box);
                    Say("crane_delivered");
                }
                if (muleLoaded && !muleSent && mule != null && Flat(p, new Vector3(restrictedArea.center.x, 0f, restrictedArea.center.y)) > 110f)
                {
                    muleSent = true;
                    mule.Go();
                }
            }
        }

        private void UpdateAcquisitionLegacy(float dt)
        {
            var lp = puzzleKind == "Acquire" ? puzzle as LockPuzzle : null;
            if (acquireBeat >= 0f)
            {
                // Solved before the spreader seated (a very fast solve): finish the descent first, then lift.
                if (spreaderDrop < 1f)
                {
                    spreaderAlign = 1f;
                    if (alignStopped < 0f) alignStopped = Time.time;
                    spreaderDrop = Mathf.MoveTowards(spreaderDrop, 1f, dt * 2.5f);
                    lockTurn = Mathf.MoveTowards(lockTurn, 1f, dt * 4f);
                    ApplyAcquisition(false);
                    return;
                }
                acquireBeat += dt;
                float u = Mathf.Clamp01(acquireBeat / BeatSeconds);
                cargoLift = Mathf.Lerp(1.2f, LiftedHeight, u * u * (3f - 2f * u));
                if (acquireBeat >= BeatSeconds)
                {
                    acquireBeat = -1f;
                    HoldCar(false);
                    Steal();
                }
            }
            else if (lp != null)
            {
                // The trolley traverses until the spreader hangs square over KX-7 (and the swing settles), the spreader
                // lowers onto the corner castings, the twistlocks turn, the seal breaks, then the hoist takes up the load.
                if (acquireStep == 0)
                {
                    acquireStep = 1;
                    Cue(hydraulicClip, 0.55f);
                    Warn("SPREADER ALIGNING OVER KX-7", false, 2.5f);
                }
                spreaderAlign = Mathf.MoveTowards(spreaderAlign, 1f, dt / AlignSeconds);
                if (spreaderAlign >= 1f && alignStopped < 0f) alignStopped = Time.time;
                if (alignStopped >= 0f && Time.time - alignStopped > 0.5f)
                {
                    if (acquireStep == 1) { acquireStep = 2; Cue(hydraulicClip, 0.7f); Warn("ALIGNED — SPREADER LOWERING", false, 2.5f); }
                    spreaderDrop = Mathf.MoveTowards(spreaderDrop, 1f, dt * 0.7f);
                    if (spreaderDrop >= 1f && acquireStep == 2) { acquireStep = 3; Cue(metalImpactClip, 0.45f); Warn("SPREADER SEATED — ENGAGE THE TWISTLOCKS", false, 3f); }
                }
                lockTurn = Mathf.MoveTowards(lockTurn, lp.Stage >= 1 ? 1f : 0f, dt * 2.5f);
                if (lp.Stage >= 1 && acquireStep == 3) { acquireStep = 4; Cue(metalImpactClip, 0.35f); Warn("TWISTLOCKS LOCKED — 4/4", false, 3f); }
                if (lp.Stage >= 2 && !sealBroken)
                {
                    sealBroken = true;
                    Cue(sparkClip);
                    Cue(metalImpactClip, 0.6f);
                    Warn("SEAL BROKEN — TAKE UP THE LOAD", false, 3f);
                    if (sealSparks != null && TargetBay < bayContainers.Length)
                    {
                        sealSparks.transform.position = bayContainers[TargetBay].position + new Vector3(0f, 1.2f, -6.2f);
                        sealSparks.Emit(80);
                    }
                }
                cargoLift = spreaderDrop >= 1f ? lp.LiftProgress * 1.2f : 0f;
            }
            else if (Current < Phase.Escape)
            {
                spreaderDrop = Mathf.MoveTowards(spreaderDrop, 0f, dt * 0.5f);
                lockTurn = Mathf.MoveTowards(lockTurn, 0f, dt);
                if (spreaderDrop <= 0f) { spreaderAlign = Mathf.MoveTowards(spreaderAlign, 0f, dt / AlignSeconds); alignStopped = -1f; acquireStep = 0; }
                cargoLift = 0f;
            }
            // While the lock puzzle runs the chase camera holds a wide, high framing of the car and the bay, so the
            // spreader's descent and the locks are in view behind the console (control is held anyway).
            bool frame = lp != null && lp.Status == HarborPuzzle.State.Running && TargetBay < bayContainers.Length && bayContainers[TargetBay] != null;
            if (frame != acquireFraming && entry != null && entry.ChaseCamera != null)
            {
                acquireFraming = frame;
                entry.ChaseCamera.SetFraming(frame, frame ? bayContainers[TargetBay].position + Vector3.up * 5f : Vector3.zero, 30f, 4.5f);
            }
            // The spreader's work light is on while it works the box (alignment → lift beat).
            if (TargetBay < spreaders.Length && spreaders[TargetBay] != null)
            {
                var wl = spreaders[TargetBay].Find("WorkLight");
                if (wl != null && wl.TryGetComponent(out Light work)) work.enabled = lp != null || acquireBeat >= 0f;
            }
            // Rainwater and spray streaming off the box as it comes up.
            if (liftRunoff != null)
            {
                var e = liftRunoff.emission;
                bool lifting = cargoLift > 0.3f && TargetBay < bayContainers.Length && bayContainers[TargetBay] != null;
                e.rateOverTime = lifting ? Mathf.Lerp(140f, 40f, cargoLift / LiftedHeight) : 0f;
                if (lifting) liftRunoff.transform.position = bayContainers[TargetBay].position + Vector3.up * 0.1f;
            }
            ApplyAcquisition(false);
        }

        private void ApplyAcquisition(bool all)
        {
            block ??= new MaterialPropertyBlock();
            for (int i = 0; i < spreaders.Length && i < spreaderRest.Length; i++)
            {
                var sp = spreaders[i];
                if (sp == null) continue;
                bool target = i == TargetBay && !all;
                // Traverse offset while aligning, plus a small pendulum swing that decays once the trolley stops.
                float a = target ? spreaderAlign * spreaderAlign * (3f - 2f * spreaderAlign) : 0f;
                float since = target && alignStopped >= 0f ? Time.time - alignStopped : 0f;
                float sway = target ? Mathf.Sin(Time.time * 2.3f) * (alignStopped >= 0f ? 0.22f * Mathf.Exp(-since * 2.2f) : 0.12f * (1f - a)) : 0f;
                sp.localPosition = spreaderRest[i] + Vector3.right * ((1f - a) * (target ? AlignOffset : 0f) + sway)
                                   - Vector3.up * ((target ? spreaderDrop : 0f) * SpreaderTravel) + Vector3.up * (target ? cargoLift : 0f);
                var cables = sp.Find("Cables");
                if (cables != null) cables.localScale = new Vector3(1f, Mathf.Max(0.5f, 24f - (sp.position.y + 1.3f)), 1f);   // hoist ropes up to the gantry
                var locks = sp.Find("Locks");
                if (locks != null) foreach (Transform l in locks) l.localRotation = Quaternion.Euler(0f, (target ? lockTurn : 0f) * 90f, 0f);
                var lamp = sp.Find("Lamp");
                if (lamp != null && lamp.TryGetComponent(out Renderer lr))
                {
                    bool flash = Mathf.Repeat(Time.time * 3f, 1f) < 0.5f;
                    lr.GetPropertyBlock(block);
                    block.SetColor(EmissionColor, !target ? new Color(1.2f, 0.6f, 0.05f) : cargoLift > 1.3f ? (flash ? new Color(6f, 0.3f, 0.2f) : new Color(0.4f, 0f, 0f))
                                                  : sealBroken ? new Color(0.3f, 5f, 0.8f) : new Color(5f, 2.6f, 0.2f));
                    lr.SetPropertyBlock(block);
                }
            }
            for (int i = 0; i < bayContainers.Length && i < containerRest.Length; i++)
                if (bayContainers[i] != null) bayContainers[i].position = containerRest[i] + Vector3.up * (i == TargetBay && !all ? cargoLift : 0f);
        }

        // ---------------- Security ----------------

        private void UpdateSecurity(Vector3 p, float speed, float dt)
        {
            float rate = 0f;
            bool inRestricted = restrictedArea.Contains(new Vector2(p.x, p.z));
            foreach (var c in cameras)
            {
                if (c == null || !c.Restricted || c.Offline) continue;
                if ((c.transform.position - p).sqrMagnitude > 60f * 60f) continue;
                if (c.Check(p)) rate += 0.32f;
            }
            foreach (var d in drones) if (d != null && d.Spots(p) && (inRestricted || Current >= Phase.Breach)) rate += 0.45f;
            if (Patrol != null && Patrol.Vehicle != null && (Patrol.Vehicle.Body.position - p).magnitude < 22f && (inRestricted || speed > 20f)) rate += 0.35f;
            // Police on patrol notice the same things the scripted patrol did: a car close by inside the restricted area or speeding.
            if (police != null)
                foreach (var u in police.Units)
                    if (u.Car != null && (u.Car.Body.position - p).magnitude < 22f && (inRestricted || speed > 20f)) { rate += 0.35f; break; }

            bool spotted = rate > 0f;
            if (spotted && !wasSpotted && !AlarmRaised) { Warn("SPOTTED — SECURITY ALERT RISING", true, 3f); Say("spotted"); }
            wasSpotted = spotted;
            if (!AlarmRaised)
            {
                Alert = Mathf.Clamp01(Alert + (spotted ? rate : -0.06f) * dt);
                if (Alert > MaxAlert) { bool crossed = MaxAlert < 0.6f && Alert >= 0.6f; MaxAlert = Alert; if (crossed) Optional(); }
                if (Alert >= 1f) RaiseAlarm("SECURITY ALARM — YOU WERE SEEN");
            }
        }

        private void RaiseAlarm(string reason)
        {
            if (AlarmRaised) return;
            AlarmRaised = true;
            Alert = MaxAlert = 1f;
            StartLockdown(escapeSeconds + earlyAlarmBonus);
            if (heistAlarm != null) heistAlarm.Trigger();
            foreach (var sl in searchlights) if (sl != null && vehicle != null) sl.Track(vehicle.transform, vehicle.transform.position);
            if (hud != null) hud.Banner("ALARM RAISED", new Color(1f, 0.3f, 0.25f));
            Warn(reason, true, 8f);
            Warn("LOCKDOWN CLOCK STARTED EARLY — FINISH THE JOB", true, 8f);
            Say("alarm_early");
            Optional();
        }

        private void StartLockdown(float seconds)
        {
            if (lockdown) return;
            lockdown = true;
            lockdownClock = 0f;
            lockdownTotal = seconds;
            if (mainGate != null) mainGate.SetOpen(false);
            if (alarmLoop != null) alarmLoop.Play();
            if (sirenLoop != null) sirenLoop.Play();
            Cue(lockdownClip);
            Warn("SOUTH PERIMETER SEALED", true);
            bool blind = RelaySpiked;
            if (blind) blindUntil = Time.time + relayBlindSeconds;
            foreach (var d in drones) if (d != null) d.SetMode(blind ? HarborDrone.Mode.Offline : HarborDrone.Mode.Chase, vehicle.transform);
            if (Patrol != null) Patrol.Pursue(vehicle.transform, 30f);
            if (police != null) police.Alert(vehicle.Body != null ? vehicle.Body.position : vehicle.transform.position);
            if (storm != null) storm.Escalate(Mathf.Max(storm.Intensity, 0.7f), 20f);
        }

        private void Steal()
        {
            SetShipmentGlows(TargetBay, true);
            // The heist moment: the crane carries the box away to the crew's trailer, the compound alarm runs its sequence,
            // the searchlights swing onto the lifted box (then onto the car), and the police get the call (StartLockdown).
            HeistStage = 4;
            stealAt = Time.time;
            if (crane != null) crane.Deliver();
            if (heistAlarm != null) heistAlarm.Trigger();
            foreach (var sl in searchlights) if (sl != null && crane != null) sl.Track(crane.TargetBox, bayContainers[TargetBay].position);
            EscapeElapsed = 0f;
            if (!lockdown) StartLockdown(escapeSeconds);
            if (hud != null) hud.Banner("THEFT ALARM", new Color(1f, 0.3f, 0.25f));
            if (storm != null) { storm.Escalate(1f, 40f); storm.Strike(400f); }
            for (int i = 0; i < shallowFloods.Length; i++) if (shallowFloods[i] != null) shallowFloods[i].Flood(false, 10f + i * 5f);
            foreach (var f in deepFloods) if (f != null) f.Flood(false, 8f);
            if (restrictedNorthGate != null) restrictedNorthGate.SetOpen(true);
            Warn("STORM SURGE — WATER RISING ACROSS THE LOW DOCKS", false);
            Say("theft");
            Say("security_chatter");
            if (hud != null) hud.DeviceCard("CONTROL", "EXTRACTION CODE  K7-SEAWALL. East gate is in lockdown: override the junction box beside it.", true);
            Cue(deviceCritical);
            if (entry.ChaseCamera != null) { entry.ChaseCamera.CinematicBeat(bayContainers[TargetBay].position + Vector3.up * 10f, 2.4f); entry.ChaseCamera.Kick(0.1f); }
            if (Rival != null && rivalEscapeRoute.Length > 0) Rival.Follow(rivalEscapeRoute, 24f, false);
            // The roadblock: one approach to the east gate, chosen per run, revealed when the car gets close.
            ActiveBlockade = blockades.Length > 0 ? rng.Next(blockades.Length) : -1;
            Stage = EscapeStage.ToGate;
            SetPhase(Phase.Escape);
            Optional();
        }

        // ---------------- Escape ----------------

        private void UpdateEscape(Vector3 p, float speed, float dt, out Transform target, out string markerText)
        {
            EscapeElapsed += dt;
            target = extractionPoint;
            markerText = "EXTRACTION";

            if (!quayWarned && EscapeElapsed >= quayWarningDelay)
            {
                quayWarned = true;
                Cue(warningClip);
                Warn($"FLOOD WARNING: QUAYSIDE CLOSING IN {quayCloseDelay - quayWarningDelay:0} S", true);
            }
            if (!quayClosed && EscapeElapsed >= quayCloseDelay)
            {
                quayClosed = true;
                foreach (var f in deepFloods) if (f != null) f.Flood(true, 7f);
                Cue(warningClip);
                Warn("QUAYSIDE FLOODED — LOW DOCK ROAD CLOSING", true, 12f);
            }
            if (!checkpointClosed && EscapeElapsed >= checkpointCloseDelay)
            {
                checkpointClosed = true;
                if (laneCheckpoint != null) laneCheckpoint.SetOpen(false);
                Cue(warningClip);
                Warn("TERMINAL CHECKPOINT SEALED — LANE 80 EAST CLOSED", true, 10f);
                Say("checkpoint_sealed");
            }
            // The roadblock appears when the car is within 260 m of it (or 25 s after the theft).
            if (!BlockadeRevealed && ActiveBlockade >= 0 && Stage == EscapeStage.ToGate &&
                (Vector2.Distance(new Vector2(p.x, p.z), blockadeAreas[ActiveBlockade].center) < 260f || EscapeElapsed > 25f))
                RevealBlockade();
            if (blindUntil > 0f && Time.time >= blindUntil)
            {
                blindUntil = 0f;
                foreach (var d in drones) if (d != null) d.SetMode(HarborDrone.Mode.Chase, vehicle.transform);
                Warn("DRONE RELAY BACK ONLINE — DRONES RESUMING PURSUIT", true, 4f);
            }

            if (RouteTaken.Length == 0)
                for (int i = 0; i < routeProbes.Length && i < routeNames.Length; i++)
                    if (routeProbes[i] != null && Flat(p, routeProbes[i]) < 35f) { RouteTaken = routeNames[i]; break; }

            switch (Stage)
            {
                case EscapeStage.ToGate:
                    target = overridePoint; markerText = "EAST GATE";
                    if (Flat(p, overridePoint) < 60f)
                    {
                        Stage = EscapeStage.Override;
                        Say("override");
                        Objective("OVERRIDE THE GATE POWER", "The east gate is locked down. Park at the junction box beside it and reroute the power.");
                    }
                    break;
                case EscapeStage.Override:
                    target = overridePoint; markerText = "JUNCTION BOX";
                    Station(p, speed, overridePoint, "GATE JUNCTION BOX", "Override");
                    break;
                case EscapeStage.Coast:
                    if (!coastAnnounced && coastArea.Contains(new Vector2(p.x, p.z)))
                    {
                        coastAnnounced = true;
                        Say("coast");
                        Say("bypass");
                        if (storm != null) storm.Escalate(1f, 5f);
                    }
                    bool washed = waves != null && waves.Washing(p, out _);
                    if (washed && !wasWashed)
                    {
                        WaveHits++;
                        Say("wave_hit");
                        Warn("WAVE HIT — GRIP LOST, EASE OFF", true, 3f);
                        if (entry.ChaseCamera != null) entry.ChaseCamera.Kick(0.15f);
                        Optional();
                    }
                    wasWashed = washed;
                    if (Flat(p, extractionPoint) < reachRadius) Extract();
                    break;
            }

            if (Current == Phase.Escape && RemainingTime() <= 0f) SetPhase(Phase.Failed);
            if (rivalCoasting && !rivalEscaped && Rival != null && Rival.Vehicle != null && Rival.Arrived &&
                Flat(Rival.Vehicle.Body.position, extractionPoint) < reachRadius * 2f)
            {
                rivalEscaped = true;
                Warn("RIVAL CREW REACHED THE EXTRACTION FIRST", true, 6f);
            }
        }

        private void RevealBlockade()
        {
            BlockadeRevealed = true;
            var b = blockades[ActiveBlockade];
            if (b != null) b.SetActive(true);
            string where = ActiveBlockade < blockadeNames.Length ? blockadeNames[ActiveBlockade] : "THE ROAD AHEAD";
            Warn($"ROADBLOCK: PATROL ON {where}", true, 6f);
            Say("blockade");
            if (hud != null) hud.DeviceCard("OVERWATCH", $"DRONE FEED: patrol units blocking {where}. Rerouting.", true);
            Cue(deviceCritical);
            if (entry.ChaseCamera != null && b != null) entry.ChaseCamera.Focus(b.transform.position + Vector3.up * 2f, 2.5f, 0.35f);
            routeTimer = 0f;
        }

        private void Extract()
        {
            SetPhase(Phase.Extracted);
            Say("extracted");
            if (entry.ChaseCamera != null) entry.ChaseCamera.SetFraming(true, extractionPoint.position + Vector3.up * 4f, 140f, 5f);
            if (hud != null) hud.ShowLetterbox(true);
            HoldCar(true);
            string results = Results();
            LastUnlocks = garage.RecordResult(true, LastGrade);
            if (LastUnlocks.Count > 0)
            {
                var sb = new StringBuilder(results);
                foreach (var car in LastUnlocks) sb.Insert(0, $"NEW CAR UNLOCKED:  {car.DisplayName}\n");
                results = sb.ToString();
                if (hud != null) hud.DeviceCard("CONTROL", $"Garage access granted: {string.Join(", ", LastUnlocks.ConvertAll(c => c.DisplayName))}.", true);
                Cue(deviceNotify);
            }
            if (CampaignMode) results = results.Replace("E  REPLAY      G  GARAGE", "E / TAP  ·  CONTINUE");
            if (hud != null) hud.ShowResults(true, "OPERATION COMPLETE", results);
            Extracted?.Invoke(LastGrade, Time.time - missionStart);
        }

        private float RemainingTime() => lockdownTotal - lockdownClock - penalties;
        public float LockdownLeft => RemainingTime();

        private void UpdateOpposition(Vector3 p, float dt)
        {
            if (lockdown && Current is not (Phase.Extracted or Phase.Failed))
            {
                // Drone lock speeds the lockdown clock up by half.
                bool locked = false;
                foreach (var d in drones) if (d != null && d.Locked && d.Current == HarborDrone.Mode.Chase) locked = true;
                if (locked) Say("drone_lock");
                lockdownClock += dt * (locked ? 1.5f : 1f);
                if (lockdownClock >= lockdownDelay) SetLockdownVisuals(true, lockdownClock);
                if (hud != null) hud.SetTimer($"LOCKDOWN  {Format(Mathf.Max(0f, RemainingTime()))}{(locked ? "   ◉ DRONE LOCK" : "")}", RemainingTime() < 30f || locked);
                if (Current != Phase.Escape && RemainingTime() <= 0f) SetPhase(Phase.Failed);

                patrolCooldown -= dt;
                if (police != null && patrolCooldown <= 0f)
                    foreach (var u in police.Units)
                    {
                        if (u.Car == null || u.Car.Body == null || vehicle.Body == null) continue;
                        // A real bump (close and closing fast), not a unit stopped behind the car at a hold-short line.
                        float gap = Vector3.Distance(u.Car.Body.position, p);
                        float closing = (u.Car.Body.linearVelocity - vehicle.Body.linearVelocity).magnitude;
                        if (gap > 5.2f || closing < 1.5f) continue;
                        patrolCooldown = 4f;
                        patrolHits++;
                        penalties += 6f;
                        Warn($"POLICE CONTACT ({u.Callsign}) — LOCKDOWN −6 S", true, 3f);
                        Cue(deniedClip);
                        if (entry.ChaseCamera != null) entry.ChaseCamera.Kick(0.12f);
                        break;
                    }
                if (police != null && police.Current == HarborPolice.State.Pursuit)
                {
                    float nearest = float.MaxValue;
                    foreach (var u in police.Units) nearest = Mathf.Min(nearest, u.Distance);
                    if (nearest < 24f && police.AnySees) Say("police_close");
                    if (!policeCoastCalled && coastArea.Contains(new Vector2(p.x, p.z)) && nearest < 450f) { policeCoastCalled = true; Say("police_coast"); }
                }
                if (police != null) foreach (var d in drones) if (d != null && d.Locked && d.Current == HarborDrone.Mode.Chase) police.Report(p);
                if (Patrol != null && Patrol.Vehicle != null && patrolCooldown <= 0f && (Patrol.Vehicle.Body.position - p).magnitude < 7f)
                {
                    patrolCooldown = 4f;
                    patrolHits++;
                    penalties += 6f;
                    Warn("PATROL CONTACT — LOCKDOWN −6 S", true, 3f);
                    Cue(deniedClip);
                }
            }

            if (Rival != null && Rival.Vehicle != null)
            {
                if (rivalLaunched && !rivalTripped && Current < Phase.Escape)
                {
                    if (Flat(Rival.Vehicle.Body.position, securityGatePoint) < 200f) Say("rival_close");
                    if (Rival.Arrived) { rivalTripped = true; RaiseAlarm("RIVAL CREW HIT THE RESTRICTED AREA FIRST — ALARM TRIPPED"); }
                }
            }
        }

        private void LaunchRival()
        {
            if (Rival == null || rivalRoute.Length < 2) return;
            rivalLaunched = true;
            Rival.Follow(rivalRoute, 17f, false);
            Warn("RIVAL CREW MOVING ON THE CARGO FROM THE FREIGHT YARD", true, 7f);
            Say("rival");
        }

        // ---------------- Command centre routing ----------------

        /// <summary>Closures and hazards from the live world state (floods, gates, roadblock, waves, drones, rival).</summary>
        private void GatherThreats()
        {
            routes.Closures.Clear();
            routes.Hazards.Clear();
            foreach (var f in deepFloods) if (f != null && f.Closed) routes.Closures.Add((f.Area, f.Label));
            void Gate(HarborGate g, string label)
            {
                if (g == null || g.IsOpen) return;
                var c = g.transform.position;
                routes.Closures.Add((new Rect(c.x - 6f, c.z - 6f, 12f, 12f), label));
            }
            Gate(laneCheckpoint, "LANE 80 CHECKPOINT");
            if (lockdown) Gate(mainGate, "SOUTH GATE");   // before the lockdown it opens for the forged pass
            if (Current != Phase.Breach) Gate(securityGate, "SECURITY GATE");
            if (Stage == EscapeStage.Coast) Gate(extractionGate, "EAST GATE");
            if (BlockadeRevealed && ActiveBlockade >= 0) routes.Closures.Add((blockadeAreas[ActiveBlockade], ActiveBlockade < blockadeNames.Length ? blockadeNames[ActiveBlockade] : "ROADBLOCK"));
            if (waves != null)
                foreach (var s in waves.Sections)
                    if (HarborWaves.Dangerous(s)) routes.Hazards.Add((s.road, 420f, s.label));
            foreach (var d in drones)
                if (d != null && d.Current == HarborDrone.Mode.Chase && d.Locked)
                {
                    var c = d.transform.position;
                    routes.Hazards.Add((new Rect(c.x - 40f, c.z - 40f, 80f, 80f), 120f, "DRONE SWEEP"));
                }
            foreach (var f in shallowFloods) if (f != null && f.Dangerous) routes.Hazards.Add((f.Area, 150f, f.Label));
            if (Rival != null && Rival.Vehicle != null && rivalLaunched)
            {
                var c = Rival.Vehicle.Body.position;
                routes.Hazards.Add((new Rect(c.x - 25f, c.z - 25f, 50f, 50f), 60f, "RIVAL CREW"));
            }
            if (police != null)
            {
                // Units in pursuit (and an interceptor waiting at a junction) make their roads costly: CONTROL steers the
                // driver round them, which is how the units pressure the car toward another route.
                if (police.Current is HarborPolice.State.Pursuit or HarborPolice.State.Alert)
                    foreach (var u in police.Units)
                    {
                        if (u.Car == null) continue;
                        var c = u.Car.Body.position;
                        float half = u.Intercepting ? 30f : 18f;
                        routes.Hazards.Add((new Rect(c.x - half, c.z - half, half * 2f, half * 2f), u.Intercepting ? 160f : 50f, "POLICE " + u.Callsign));
                    }
                // Mirror the live closures onto the police road graph (the same closures CONTROL routes round).
                var closed = new HashSet<string>();
                foreach (var c in routes.Closures) closed.Add(c.label);
                if (restrictedNorthGate != null && !restrictedNorthGate.IsOpen) closed.Add("NORTH GATE");
                if (securityGate != null && !securityGate.IsOpen) closed.Add("SECURITY GATE");
                if (extractionGate != null && !extractionGate.IsOpen) closed.Add("EAST GATE");
                if (mainGate != null && !mainGate.IsOpen && lockdown) closed.Add("SOUTH GATE");
                if (laneCheckpoint != null && !laneCheckpoint.IsOpen) closed.Add("LANE 80 CHECKPOINT");
                if (waves != null) foreach (var s in waves.Sections) if (HarborWaves.Dangerous(s)) closed.Add("WAVE:" + s.label);
                foreach (var id in PoliceClosureIds) police.SetClosed(id, closed.Contains(id));
                foreach (var n in blockadeNames) police.SetClosed(n, closed.Contains(n));
            }
        }

        private void UpdateRoute(Vector3 p, Transform target, float dt)
        {
            if (routes == null) return;
            routeTimer -= dt;
            if (target == null || Current is Phase.Extracted or Phase.Failed or Phase.Briefing) { route.Clear(); if (hud != null && hud.Minimap != null) hud.Minimap.SetRoute(null, false); return; }
            if (routeTimer > 0f) return;
            routeTimer = 1f;
            GatherThreats();
            var closures = new StringBuilder();
            foreach (var c in routes.Closures) closures.Append(c.label).Append('|');
            string closureSig = closures.ToString();
            bool ok = routes.Find(p, target.position, route, out _, out var blockedBy);
            RouteBlockedBy = ok ? null : blockedBy;
            string sig = ok ? routes.Signature(route) : "none";
            // Announce a reroute only when a NEW closure changed the route (not when the car simply took another road).
            if (Current == Phase.Escape && sig != routeSignature && closureSig != closureSignature && routeSignature.Length > 0)
            {
                if (!ok)
                {
                    Say("no_route");
                    Warn($"NO CLEAR ROUTE — {blockedBy}", true, 5f);
                }
                else
                {
                    Reroutes++;
                    rerouteFlashUntil = Time.time + 10f;
                    if (!BlockadeRevealed || Reroutes > 1) Say(quayClosed && closureSig.Contains("QUAY") ? "flood_close" : "reroute");
                    Warn("CONTROL: ROUTE UPDATED — FOLLOW THE LINE", false, 4f);
                }
            }
            if (sig != routeSignature) routeSignature = sig;
            closureSignature = closureSig;
            if (hud != null && hud.Minimap != null) hud.Minimap.SetRoute(ok ? route : null, Time.time < rerouteFlashUntil);
        }

        private void OnWave(HarborWaves.Section s, HarborWaves.State state)
        {
            if (Current != Phase.Escape || Stage != EscapeStage.Coast || vehicle == null) return;
            float d = Vector2.Distance(new Vector2(vehicle.Body.position.x, vehicle.Body.position.z), s.road.center);
            if (d > 320f) return;
            switch (state)
            {
                case HarborWaves.State.Building:
                    Say("wave_warn");
                    Warn($"{s.label}: WAVE INCOMING — HOLD SHORT", true, 5f);
                    break;
                case HarborWaves.State.Impact:
                    // The set itself is framed by HarborCoastCamera (wave reveal); here only a restrained jolt, scaled by distance.
                    if (d < 120f && entry.ChaseCamera != null) entry.ChaseCamera.Kick(Mathf.Lerp(0.28f, 0.06f, d / 120f));
                    break;
                case HarborWaves.State.Receding:
                    Say("wave_clear");
                    break;
            }
        }

        // ---------------- Floods ----------------

        private void UpdateFloods(Vector3 p, float dt)
        {
            float depth = 0f;
            void Tick(FloodZone f)
            {
                if (f == null) return;
                bool wasDangerous = f.Dangerous, wasClosed = f.Closed;
                f.Tick(dt, p);
                if (f.Contains(p)) depth = Mathf.Max(depth, f.Level / f.DeepLevel);
                if (!wasDangerous && f.Dangerous) Warn($"{f.Label}: DEEP WATER — SPEED PENALTY", true, 6f);
                if (!wasClosed && f.Closed) { Warn($"{f.Label}: ROAD CLOSED", true, 10f); Cue(warningClip); if (police != null) police.LetUnitsOut(f.ClosureColliders); }
            }
            foreach (var f in deepFloods) Tick(f);
            foreach (var f in shallowFloods) Tick(f);
            if (waves != null && waves.Sample(p, out float wd, out _, out _)) depth = Mathf.Max(depth, Mathf.Clamp01(wd / 0.25f));
            if (spray != null) spray.WaterDepth = Mathf.Clamp01(depth);
        }

        // ---------------- Radio, ambience ----------------

        private void Say(string id) { if (radio != null) radio.Say(id); }

        // ---------------- Police ----------------

        private bool policeCoastCalled;
        private static readonly string[] PoliceClosureIds =
            { "QUAYSIDE WEST", "QUAYSIDE EAST", "LANE 80 CHECKPOINT", "SOUTH GATE", "SECURITY GATE", "NORTH GATE", "EAST GATE", "WAVE:SEAWALL WEST", "WAVE:SEAWALL MID", "WAVE:SEAWALL EAST" };

        private void OnPoliceEvent(string id, HarborPolice.Unit unit)
        {
            if (Current is Phase.Extracted or Phase.Failed) return;
            switch (id)
            {
                case "alert":
                    Say("police_dispatch");
                    Warn("POLICE RESPONDING — 3 UNITS", true, 4f);
                    break;
                case "pursuit":
                    Say("police_pursuit_" + police.PlayerHeading());
                    Warn($"POLICE PURSUIT — {(unit != null ? unit.Callsign : "UNITS")} HAS VISUAL", true, 4f);
                    break;
                case "intercept":
                    Say("police_intercept");
                    Warn($"{(unit != null ? unit.Callsign : "POLICE")}: MOVING TO BLOCK THE ROAD AHEAD", true, 4f);
                    routeTimer = 0f;
                    break;
                case "lost":
                    Say("police_lost");
                    Say("police_lost_control");
                    Warn("POLICE LOST VISUAL — THEY ARE SEARCHING", false, 4f);
                    break;
                case "reacquired":
                    Say("police_reacquired");
                    Warn("POLICE HAVE YOU AGAIN", true, 3f);
                    break;
            }
        }

        private bool NoStopping(Vector3 at)
        {
            var q = new Vector2(at.x, at.z);
            if (waves != null) foreach (var s in waves.Sections) if (s.road.Contains(q)) return true;
            foreach (var f in deepFloods) if (f != null && f.Area.Contains(q)) return true;
            foreach (var f in shallowFloods) if (f != null && f.Area.Contains(q)) return true;
            foreach (var a in blockadeAreas) if (a.Contains(q)) return true;
            return false;
        }

        private float WaterDepthAt(Vector3 at)
        {
            float depth = 0f;
            foreach (var f in deepFloods) if (f != null && f.Contains(at)) depth = Mathf.Max(depth, f.Level);
            foreach (var f in shallowFloods) if (f != null && f.Contains(at)) depth = Mathf.Max(depth, f.Level);
            if (waves != null && waves.Sample(at, out float wd, out _, out _)) depth = Mathf.Max(depth, wd);
            return depth;
        }

        private void UpdateRadioAmbience(float dt)
        {
            float intensity = storm != null ? storm.Intensity : 0.5f;
            if (radio != null) radio.Interference(Mathf.InverseLerp(0.6f, 1f, intensity));
            interferenceTimer -= dt;
            if (hud != null && interferenceTimer <= 0f && intensity > 0.75f)
            {
                interferenceTimer = Random.Range(3f, 8f);
                hud.Interference(0.25f);
            }
            if (hud != null && waves != null) hud.SetLens(waves.LensWater);
        }

        private void ToggleDevice()
        {
            if (hud == null) return;
            float speed = vehicle != null && vehicle.Body != null ? vehicle.Body.linearVelocity.magnitude : 0f;
            if (!hud.DeviceExpanded && speed > 3f) { Warn("MESSAGE LOG: STOP THE CAR TO READ", false, 2f); return; }
            hud.SetDeviceExpanded(!hud.DeviceExpanded);
        }

        // ---------------- HUD ----------------

        private void UpdateHud(Vector3 p, Transform target, string markerText)
        {
            if (hud == null) return;
            // Far from the objective, the marker follows CONTROL's route 70 m ahead.
            Vector3? markerAt = target != null ? target.position : null;
            if (target != null && route.Count > 1 && Flat(p, target) > 140f) { markerAt = HarborRoutes.Lookahead(route, 70f); markerText = $"ROUTE  ·  {markerText}"; }
            HoldShort = false;
            if (Current == Phase.Escape && Stage == EscapeStage.Coast && waves != null && route.Count > 1)
            {
                var ahead = HarborRoutes.Lookahead(route, 90f);
                foreach (var s in waves.Sections)
                    if (HarborWaves.Dangerous(s) && !s.road.Contains(new Vector2(p.x, p.z)) && s.road.Contains(new Vector2(ahead.x, ahead.z)) || HarborWaves.Dangerous(s) && Mathf.Abs(p.x - s.road.xMin) < 70f && p.x < s.road.xMin && s.road.yMin < p.z + 10f)
                    {
                        HoldShort = true;
                        markerAt = new Vector3(s.road.xMin - 6f, 0f, s.road.center.y);
                        markerText = $"HOLD SHORT  ·  {s.label} WAVE";
                    }
            }
            hud.SetMarker(puzzle == null ? Camera.main : null, markerAt, markerText);
            hud.SetDistance(target != null ? Flat(p, target) : -1f);
            string level = AlarmRaised || lockdown ? "ALARM" : Alert > 0.6f ? "ALERTED" : Alert > 0.15f ? "SUSPICIOUS" : "CALM";
            hud.SetAlert(lockdown ? 1f : Alert, $"SECURITY  ·  {level}", AlarmRaised || lockdown);

            // The status text is rebuilt five times a second (it allocates; nothing in it changes faster than that).
            bool statusDue = Time.unscaledTime >= nextStatus;
            if (statusDue) nextStatus = Time.unscaledTime + 0.2f;
            var status = statusDue ? statusText.Clear() : null;
            if (statusDue)
            {
            status.Append($"STORM {Mathf.RoundToInt((storm != null ? storm.Intensity : 0f) * 100f)}%   WIND {(storm != null ? storm.Wind.magnitude * 3.6f : 0f):0} KM/H NE");
            foreach (var f in deepFloods) if (f != null && f.Flooding) status.Append($"\n{f.Label}: {(f.Closed ? "CLOSED" : f.Dangerous ? "DEEP" : "RISING")}");
            foreach (var f in shallowFloods) if (f != null && f.Flooding) status.Append($"\n{f.Label}: {(f.Dangerous ? "DEEP" : "STANDING WATER")}");
            if (checkpointClosed) status.Append("\nLANE 80 CHECKPOINT: SEALED");
            if (BlockadeRevealed && ActiveBlockade >= 0) status.Append($"\nROADBLOCK: {(ActiveBlockade < blockadeNames.Length ? blockadeNames[ActiveBlockade] : "")}");
            if (waves != null && Stage == EscapeStage.Coast)
                foreach (var s in waves.Sections) status.Append($"\n{s.label}: {(s.state == HarborWaves.State.Calm ? "CLEAR" : HarborWaves.Dangerous(s) ? "WAVE — HOLD" : s.state.ToString().ToUpperInvariant())}");
            if (RouteBlockedBy != null) status.Append($"\nNO CLEAR ROUTE: {RouteBlockedBy}");
            if (police != null && police.Current != HarborPolice.State.Patrol)
            {
                float nearest = float.MaxValue;
                foreach (var u in police.Units) nearest = Mathf.Min(nearest, u.Distance);
                string ps = police.Current switch
                {
                    HarborPolice.State.Alert => "RESPONDING",
                    HarborPolice.State.Pursuit => police.AnySees ? $"PURSUIT · {nearest:0} M" : "PURSUIT · NO VISUAL",
                    HarborPolice.State.Search => $"SEARCHING · LOST YOU {police.SinceSeen:0} S AGO",
                    _ => "STOOD DOWN"
                };
                status.Append($"\nPOLICE: {ps}");
                foreach (var u in police.Units) if (u.Intercepting) { status.Append($"\n{u.Callsign}: BLOCKING AHEAD"); break; }
            }
            if (rivalLaunched && Rival != null && Rival.Vehicle != null && Current < Phase.Escape)
                status.Append($"\nRIVAL CREW: {Flat(Rival.Vehicle.Body.position, securityGatePoint):0} M FROM CARGO");
            if (blindUntil > 0f) status.Append($"\nDRONES BLIND: {blindUntil - Time.time:0} S");
            hud.SetStatus(status.ToString());
            }

            if (hud.Minimap == null) return;
            var zones = minimapZones;
            zones.Clear();
            foreach (var f in deepFloods) if (f != null && f.Flooding) zones.Add((f.Area, f.Closed ? new Color(1f, 0.2f, 0.15f, 0.85f) : f.Dangerous ? new Color(1f, 0.6f, 0.1f, 0.8f) : new Color(0.2f, 0.5f, 1f, 0.7f)));
            foreach (var f in shallowFloods) if (f != null && f.Flooding) zones.Add((f.Area, new Color(0.2f, 0.5f, 1f, 0.7f)));
            if (laneCheckpoint != null && !laneCheckpoint.IsOpen)
            {
                var c = laneCheckpoint.transform.position;
                zones.Add((new Rect(c.x - 4f, c.z - 10f, 8f, 20f), new Color(1f, 0.2f, 0.15f, 0.9f)));
            }
            if (BlockadeRevealed && ActiveBlockade >= 0) zones.Add((blockadeAreas[ActiveBlockade], new Color(1f, 0.15f, 0.1f, 0.95f)));
            if (waves != null)
                foreach (var s in waves.Sections)
                    zones.Add((s.road, s.state == HarborWaves.State.Calm ? new Color(1f, 0.7f, 0.2f, 0.35f) : HarborWaves.Dangerous(s) ? new Color(1f, 0.25f, 0.2f, 0.85f) : new Color(1f, 0.7f, 0.2f, 0.7f)));
            var markers = minimapMarkers;
            markers.Clear();
            markers.Add(new() { Position = extractionPoint.position, Colour = new Color(0.3f, 1f, 0.5f), Size = 6f });
            if (relayPoint != null && !RelaySpiked && Current < Phase.Escape) markers.Add(new() { Position = relayPoint.position, Colour = new Color(0.7f, 0.6f, 1f), Size = 4f });
            if (Rival != null && Rival.Vehicle != null && rivalLaunched) markers.Add(new() { Position = Rival.Vehicle.Body.position, Colour = new Color(1f, 0.3f, 0.9f), Size = 5f });
            if (Patrol != null && Patrol.Vehicle != null) markers.Add(new() { Position = Patrol.Vehicle.Body.position, Colour = new Color(0.3f, 0.5f, 1f), Size = 5f });
            if (police != null)
            {
                bool flash = Mathf.Repeat(Time.time * 2.5f, 1f) < 0.5f;
                bool hot = police.Current is HarborPolice.State.Alert or HarborPolice.State.Pursuit;
                foreach (var u in police.Units)
                    if (u.Car != null) markers.Add(new() { Position = u.Car.Body.position, Colour = hot ? (flash ? new Color(1f, 0.2f, 0.2f) : new Color(0.25f, 0.45f, 1f)) : new Color(0.3f, 0.5f, 1f), Size = hot ? 6f : 4.5f });
            }
            foreach (var d in drones) if (d != null && d.Current != HarborDrone.Mode.Offline) markers.Add(new() { Position = d.transform.position, Colour = new Color(1f, 0.25f, 0.2f), Size = 3f });
            hud.Minimap.SetFrame(p, vehicle.transform.eulerAngles.y, zones, markers, target != null ? target.position : null);
        }

        private readonly StringBuilder statusText = new();
        private readonly List<(Rect, Color)> minimapZones = new();
        private readonly List<HarborMinimap.Marker> minimapMarkers = new();
        private float nextStatus;

        private string Results()
        {
            float left = Mathf.Max(0f, RemainingTime());
            bool ghost = MaxAlert < 0.6f;
            int optionals = (RelaySpiked ? 1 : 0) + (ghost ? 1 : 0) + (WaveHits == 0 ? 1 : 0);
            float score = left + (AlarmRaised ? 0f : 40f) + (rivalEscaped ? -25f : 20f) - patrolHits * 5f - PuzzleMistakes * 4f - WaveHits * 8f + optionals * 15f;
            LastGrade = score >= 190f ? "S" : score >= 130f ? "A" : score >= 70f ? "B" : "C";
            return $"GRADE   {LastGrade}\n\nTOTAL TIME   {Format(Time.time - missionStart)}     ESCAPE   {Format(EscapeElapsed)}     LOCKDOWN LEFT   {Format(left)}\n" +
                   $"ROUTE   {(RouteTaken.Length > 0 ? RouteTaken : "—")}     CONTROL REROUTES   {Reroutes}\n" +
                   $"PUZZLE MISTAKES   {PuzzleMistakes}     PEAK SUSPICION   {Mathf.RoundToInt(MaxAlert * 100f)}%     ALARM BEFORE THEFT   {(AlarmRaised ? "YES" : "NO")}\n" +
                   $"WAVE HITS   {WaveHits}     PATROL CONTACTS   {patrolHits}     RIVAL CREW   {(rivalEscaped ? "BEAT YOU OUT" : "OUTRUN")}\n" +
                   $"OPTIONAL   relay {(RelaySpiked ? "✓" : "—")}   ghost {(ghost ? "✓" : "—")}   storm runner {(WaveHits == 0 ? "✓" : "—")}\n\nE  REPLAY      G  GARAGE";
        }

        // ---------------- Input ----------------

        private void OnInteract(InputAction.CallbackContext _)
        {
            if (Current == Phase.Garage) { ConfirmGarage(); return; }
            if (Current == Phase.Briefing && cinematics != null && cinematics.Playing) { cinematics.Skip(); return; }
            if (CampaignMode && paused) return;   // the campaign pause menu takes Confirm
            if (CampaignMode && Current == Phase.Extracted && CampaignContinue != null) { CampaignContinue(); return; }
            if (Current is Phase.Extracted or Phase.Failed || paused) { ResetLevel(); return; }
            if (puzzle != null) return;
            Transform point = Current switch
            {
                Phase.Manifest => manifestPoint, Phase.Breach => securityGatePoint, Phase.Acquire => shipmentBays[TargetBay],
                Phase.Escape when Stage == EscapeStage.Override => overridePoint, _ => null
            };
            bool nearRelay = relayPoint != null && vehicle != null && Flat(vehicle.Body.position, relayPoint) < interactRadius * 4f;
            if (point != null && vehicle != null && Flat(vehicle.Body.position, point) > interactRadius * 4f && !nearRelay)
                Denied("NOTHING TO INTERACT WITH HERE — FOLLOW THE MARKER");
        }

        private void OnPause(InputAction.CallbackContext _)
        {
            if (Current is Phase.Extracted or Phase.Failed or Phase.Garage) return;
            if (CampaignMode && Current == Phase.Inactive) return;   // the campaign trailer: Start / Esc skips, never pauses
            SetPaused(!paused);
        }

        private void SetPaused(bool on)
        {
            if (paused == on) return;
            paused = on;
            if (on) { pausedScale = Time.timeScale; Time.timeScale = 0f; }
            else Time.timeScale = pausedScale;
            AudioListener.pause = on;
            if (CampaignPause != null) CampaignPause(on);
            else if (hud != null) hud.ShowPause(on);
        }

        // ---------------- Helpers ----------------

        private void SetLockdownVisuals(bool on, float t)
        {
            bool flash = on && Mathf.Repeat(t * 1.6f, 1f) < 0.5f;
            block ??= new MaterialPropertyBlock();
            foreach (var r in securityLamps)
            {
                if (r == null) continue;
                r.GetPropertyBlock(block);
                block.SetColor(EmissionColor, !on ? new Color(0.1f, 1.6f, 2.2f) : flash ? new Color(7f, 0.25f, 0.15f) : new Color(0.6f, 0.02f, 0.01f));
                r.SetPropertyBlock(block);
            }
            foreach (var l in alarmLights)
            {
                if (l == null) continue;
                l.enabled = on;
                l.intensity = flash ? 60f : 8f;
            }
        }

        /// <summary>-1: all bays neutral (manifest unread). Otherwise the target bay glows cyan (magenta once stolen).</summary>
        private void SetShipmentGlows(int target, bool stolen = false)
        {
            block ??= new MaterialPropertyBlock();
            for (int i = 0; i < shipmentGlows.Length; i++)
            {
                var r = shipmentGlows[i];
                if (r == null) continue;
                r.GetPropertyBlock(block);
                block.SetColor(EmissionColor, target < 0 ? new Color(0.5f, 0.55f, 0.6f) : i != target ? new Color(0.05f, 0.05f, 0.05f)
                                              : stolen ? new Color(4f, 0.4f, 2.6f) : new Color(0.2f, 2.2f, 3f));
                r.SetPropertyBlock(block);
            }
        }

        private void Warn(string text, bool severe, float seconds = 7f)
        {
            if (hud != null) hud.Warning(text, severe, seconds);
        }

        private void Cue(AudioClip clip, float volume = 1f)
        {
            if (cues != null && clip != null) cues.PlayOneShot(clip, volume);
        }

        private static string BayName(int i) => i switch { 0 => "R-1", 1 => "R-2", _ => "R-3" };

        private static float Flat(Vector3 p, Transform t) => t == null ? float.MaxValue : Flat(p, t.position);

        private static float Flat(Vector3 a, Vector3 b)
        {
            var d = b - a;
            d.y = 0f;
            return d.magnitude;
        }

        private static string Format(float seconds) => $"{(int)(seconds / 60f)}:{seconds % 60f:00.0}";
    }
}
