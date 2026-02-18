using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

public class ArenaGrinderPlayable : MonoBehaviour
{
    public enum XPCurve { Linear, Quadratic, Logarithmic }

    [Header("Progression")]
    public XPCurve curve = XPCurve.Linear;
    public int targetLevel = 10;

    [Header("Player (base)")]
    public int playerBaseHP = 20;
    public int playerBaseDamage = 2;
    public int playerDamagePerLevel = 1;

    [Header("Enemy scaling (n = enemy level)")]
    public int enemyBaseHP = 5;
    public int enemyHPPerLevel = 3;
    public float enemyBaseDamage = 1f;
    public float enemyDamagePerLevel = 0.5f;
    [Header("Enemy damage randomness")]
    [Tooltip("Skademultipel på bas+scaling för att få minskada.")]
    [Range(0.1f, 1.0f)]
    public float enemyMinMultiplier = 0.7f;

    [Tooltip("Skademultipel på bas+scaling för att få maxskada.")]
    [Range(1.0f, 2.5f)]
    public float enemyMaxMultiplier = 1.3f;

    [Tooltip("Hur 'sällsynta' spikes ska vara. Högre = oftare nära mitten.")]
    [Range(1f, 6f)]
    public float enemyDamageBias = 1f;

    [Tooltip("Extra liten chans till krit (spike) för att ibland kunna vinna.")]
    [Range(0f, 0.25f)]
    public float enemyCritChance = 0.2f;

    [Tooltip("Kritmultiplikator om crit triggas.")]
    [Range(1.2f, 2.5f)]
    public float enemyCritMultiplier = 2f;

    [Header("Enemy level rule")]
    [Tooltip("Om true: enemyLevel = playerLevel. Annars: enemyLevel = roundIndex.")]
    public bool enemyMatchesPlayerLevel = true;

    [Header("XP gain per win")]
    public int xpPerWin = 30;

    [Header("XP curve params")]
    public int linearA = 50;      // XP_next = a*L
    public int quadA = 20;        // XP_next = a*L^2
    public int logA = 120;        // XP_next = a*ln(L+1)

    // ---------- State ----------
    int level;
    int xp;
    int xpToNext;
    int wins;
    int losses;
    int roundIndex;

    int playerHP;
    int enemyHP;
    int playerMaxHP;
    int enemyMaxHP;
    int enemyLevel;

    bool roundActive;
    bool playerAlive => playerHP > 0;
    bool enemyAlive => enemyHP > 0;



    // ---------- UI ----------
    Canvas canvas;
    Slider playerHPBar, enemyHPBar;
    Text headerText, logText, playerStatsText, enemyStatsText, combatText;
    Button attackButton, nextRoundButton, restartButton;
    Image playerBox, enemyBox;

    // Level up popup
    RectTransform levelUpPanel;
    Text levelUpTitleText;
    Text levelUpBodyText;
    Button levelUpOkButton;
    bool levelUpPopupOpen;

    void Start()
    {
        EnsureEventSystem();
        BuildUI();
        ResetRun();
        StartNextRound();
    }

    void EnsureEventSystem()
    {
        if (FindAnyObjectByType<EventSystem>() != null) return;

        var es = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
        DontDestroyOnLoad(es);
    }

    void BuildUI()
    {
        // Canvas
        var canvasGO = new GameObject("ArenaUI", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvas = canvasGO.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;

        var scaler = canvasGO.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280, 720);

        // Root panel
        var root = CreatePanel(canvas.transform, "Root", new Vector2(0, 0), new Vector2(1, 1), Vector2.zero, Vector2.zero);
        root.GetComponent<Image>().color = new Color(0.08f, 0.08f, 0.09f, 1f);

        // Header
        headerText = CreateText(root.transform, "Header",
            $"Arena Grinder (Playable) | Curve={curve}",
            26, TextAnchor.MiddleCenter,
            new Vector2(0.05f, 0.88f), new Vector2(0.95f, 0.98f));

        // Mid panels: left player, right enemy
        var left = CreatePanel(root.transform, "LeftPanel", new Vector2(0.05f, 0.20f), new Vector2(0.48f, 0.85f));
        left.GetComponent<Image>().color = new Color(0.12f, 0.12f, 0.14f, 1f);

        var right = CreatePanel(root.transform, "RightPanel", new Vector2(0.52f, 0.20f), new Vector2(0.95f, 0.85f));
        right.GetComponent<Image>().color = new Color(0.12f, 0.12f, 0.14f, 1f);

        // Player visuals
        playerBox = CreateBox(left.transform, "PlayerBox", new Vector2(0.10f, 0.35f), new Vector2(0.90f, 0.80f));
        playerBox.color = new Color(0.2f, 0.6f, 0.9f, 1f);

        playerStatsText = CreateText(left.transform, "PlayerStats", "", 18, TextAnchor.UpperLeft,
            new Vector2(0.07f, 0.05f), new Vector2(0.93f, 0.33f));

        playerHPBar = CreateHPBar(left.transform, "PlayerHPBar",
            new Vector2(0.10f, 0.82f), new Vector2(0.90f, 0.88f));

        // Enemy visuals
        enemyBox = CreateBox(right.transform, "EnemyBox", new Vector2(0.10f, 0.35f), new Vector2(0.90f, 0.80f));
        enemyBox.color = new Color(0.9f, 0.35f, 0.25f, 1f);

        enemyStatsText = CreateText(right.transform, "EnemyStats", "", 18, TextAnchor.UpperLeft,
            new Vector2(0.07f, 0.05f), new Vector2(0.93f, 0.33f));

        enemyHPBar = CreateHPBar(right.transform, "EnemyHPBar",
            new Vector2(0.10f, 0.82f), new Vector2(0.90f, 0.88f));

        // Combat log area (bottom)
        var bottom = CreatePanel(root.transform, "BottomPanel", new Vector2(0.05f, 0.05f), new Vector2(0.95f, 0.18f));
        bottom.GetComponent<Image>().color = new Color(0.10f, 0.10f, 0.12f, 1f);

        combatText = CreateText(bottom.transform, "CombatText", "Tryck Attack för att slå.", 18, TextAnchor.MiddleLeft,
            new Vector2(0.02f, 0.52f), new Vector2(0.78f, 0.95f));

        logText = CreateText(bottom.transform, "LogText", "", 14, TextAnchor.UpperLeft,
            new Vector2(0.02f, 0.05f), new Vector2(0.78f, 0.50f));

        // Buttons (right side bottom)
        attackButton = CreateButton(bottom.transform, "AttackButton", "Attack",
            new Vector2(0.80f, 0.55f), new Vector2(0.95f, 0.92f), OnAttackClicked);

        nextRoundButton = CreateButton(bottom.transform, "NextRoundButton", "Next Round",
            new Vector2(0.80f, 0.30f), new Vector2(0.95f, 0.52f), OnNextRoundClicked);

        restartButton = CreateButton(bottom.transform, "RestartButton", "Restart",
            new Vector2(0.80f, 0.05f), new Vector2(0.95f, 0.27f), OnRestartClicked);
        BuildLevelUpPopup(root.transform);
        HideLevelUpPopup();
    }

    // ---------- Game loop ----------
    void ResetRun()
    {
        level = 1;
        xp = 0;
        wins = 0;
        roundIndex = 0;
        losses = 0;

        playerMaxHP = playerBaseHP;
        playerHP = playerMaxHP;

        xpToNext = Mathf.Max(1, XPToNext(level));

        headerText.text = $"Arena Grinder (Playable) | Curve={curve} | TargetLevel={targetLevel}";
        SetRoundSummary($"START: Level={level}, XP={xp}/{xpToNext}");

        Debug.Log($"=== Arena Grinder PLAYABLE START | Curve={curve} | TargetLevel={targetLevel} ===");
        Debug.Log(StatusLine());
    }

    void StartNextRound()
    {
        if (level >= targetLevel)
        {
            roundActive = false;
            attackButton.interactable = false;
            nextRoundButton.interactable = false;
            restartButton.interactable = true;
            combatText.text = $"MÅL NÅTT! Du nådde level {level}.";
            return;
        }

        roundIndex++;
        enemyLevel = enemyMatchesPlayerLevel ? level : roundIndex;

        enemyMaxHP = enemyBaseHP + enemyHPPerLevel * enemyLevel;
        enemyHP = enemyMaxHP;

        // (Valfritt) Återställ spelaren mellan rundor eller låt HP vara persistent.
        // För analys av progression brukar "reset HP per fight" vara enklare:
        playerMaxHP = playerBaseHP; // håll konstant i denna prototyp
        playerHP = playerMaxHP;

        roundActive = true;

        attackButton.interactable = true;
        nextRoundButton.interactable = false;
        restartButton.interactable = true;

        combatText.text = $"Runda {roundIndex}: Fiende L{enemyLevel}. Tryck Attack.";
        UpdateUI();
    }

    void OnAttackClicked()
    {
        if (levelUpPopupOpen) return;
        if (!roundActive) return;
        if (!playerAlive || !enemyAlive) return;


        int playerDamage = playerBaseDamage + playerDamagePerLevel * (level - 1);
        int dealt = playerDamage;
        enemyHP = Mathf.Max(0, enemyHP - dealt);

        string msg = $"Du slår för {dealt}.";

        if (enemyHP <= 0)
        {
            // Win
            wins++;
            roundActive = false;

            msg += " Fienden dör! VINST.";
            combatText.text = msg;

            GrantXPAndMaybeLevel();

            attackButton.interactable = false;
            nextRoundButton.interactable = true;

            SetRoundSummary($"Senaste: VINST (Win #{wins}) | Level {level} | XP {xp}/{xpToNext}");

            UpdateUI();
            return;
        }

        // Enemy retaliates
        bool enemyCrit;
        int enemyDmg = RollEnemyDamage(enemyLevel, out enemyCrit);
        playerHP = Mathf.Max(0, playerHP - enemyDmg);

        if (enemyCrit)
        {
            msg += $" Fienden CRITTAR för {enemyDmg}!";
            Debug.Log($"ENEMY CRIT! Round={roundIndex} EnemyL={enemyLevel} Damage={enemyDmg}");
        }
        else
        {
            msg += $" Fienden slår tillbaka för {enemyDmg}.";
        }

        if (playerHP <= 0)
        {
            // Lose -> rundan slutar men spelet fortsätter
            losses++;
            roundActive = false;
            msg += " DU FÖRLORAR.";
            combatText.text = msg;

            attackButton.interactable = false;

            // Viktigt: låt spelaren gå vidare ändå
            nextRoundButton.interactable = true;

            SetRoundSummary($"Senaste: FÖRLUST (Loss #{losses}) | Level {level} | Wins {wins} | Runda {roundIndex}");
            Debug.LogWarning($"ROUND LOST at PlayerLevel={level} vs EnemyLevel={enemyLevel} after {wins} wins.");

            UpdateUI();
            return;
        }

        combatText.text = msg;
        UpdateUI();
    }

    void GrantXPAndMaybeLevel()
    {
        xp += xpPerWin;

        int oldLevel = level;
        int oldDamage = playerBaseDamage + playerDamagePerLevel * (oldLevel - 1);
        int oldXpToNext = xpToNext;

        bool leveled = false;
        int levelsGained = 0;

        while (xp >= xpToNext && level < targetLevel)
        {
            xp -= xpToNext;
            level++;
            levelsGained++;
            xpToNext = Mathf.Max(1, XPToNext(level));
            leveled = true;

            Debug.Log($"LEVEL UP -> {level}. {StatusLine()}");
        }

        // Status var 5:e vinst (som tidigare)
        if (wins % 5 == 0)
            Debug.Log($"Win #{wins}. {StatusLine()}");

        if (leveled)
        {
            int newDamage = playerBaseDamage + playerDamagePerLevel * (level - 1);
            int newXpToNext = xpToNext;

            string title = levelsGained > 1 ? $"LEVEL UP! (+{levelsGained})" : "LEVEL UP!";
            string body =
                $"Level: {oldLevel} → {level}\n" +
                $"Damage: {oldDamage} → {newDamage}\n" +
                $"XP till nästa: {oldXpToNext} → {newXpToNext}\n" +
                $"Nuvarande XP: {xp}/{xpToNext}\n" +
                $"Wins: {wins}\n\n" +
                $"Kurva: {curve}";

            ShowLevelUpPopup(title, body);

            // Viktigt: uppdatera UI så bars/stats matchar nya leveln
            UpdateUI();
        }
    }

    void OnNextRoundClicked()
    {
        StartNextRound();
    }

    void OnRestartClicked()
    {
        ClearLog();
        ResetRun();
        StartNextRound();
    }

    // ---------- XP ----------
    int XPToNext(int L)
    {
        switch (curve)
        {
            case XPCurve.Linear:
                return linearA * L;

            case XPCurve.Quadratic:
                return quadA * L * L;

            case XPCurve.Logarithmic:
                return Mathf.RoundToInt(logA * Mathf.Log(L + 1f));

            default:
                return 100;
        }
    }

    // ---------- UI helpers ----------
    void UpdateUI()
    {
        headerText.text = $"Arena Grinder | Curve={curve} | Level {level} | Wins {wins} / Losses {losses}";
        // Bars
        playerHPBar.maxValue = Mathf.Max(1, playerMaxHP);
        playerHPBar.value = Mathf.Clamp(playerHP, 0, playerMaxHP);

        enemyHPBar.maxValue = Mathf.Max(1, enemyMaxHP);
        enemyHPBar.value = Mathf.Clamp(enemyHP, 0, enemyMaxHP);

        int playerDamage = playerBaseDamage + playerDamagePerLevel * (level - 1);
        //Fienden
        float baseDmg = enemyBaseDamage + enemyDamagePerLevel * enemyLevel;
        int minD = Mathf.Max(1, Mathf.CeilToInt(baseDmg * enemyMinMultiplier));
        int maxD = Mathf.Max(1, Mathf.CeilToInt(baseDmg * enemyMaxMultiplier)); // <-- utan crit
        int critMax = Mathf.Max(1, Mathf.CeilToInt(baseDmg * enemyMaxMultiplier * enemyCritMultiplier));

        playerStatsText.text =
            $"PLAYER\n" +
            $"Level: {level}\n" +
            $"HP: {playerHP}/{playerMaxHP}\n" +
            $"Damage: {playerDamage}\n" +
            $"XP: {xp}/{xpToNext}\n" +
            $"Wins: {wins}\n" +
            $"Losses: {losses}";

        enemyStatsText.text =
            $"ENEMY\n" +
            $"Enemy Level: {enemyLevel}\n" +
            $"HP: {enemyHP}/{enemyMaxHP}\n" +
            $"Damage: {minD}–{maxD}\n" +
            $"max DMG vid crit→{critMax}\n" +
            $"Round: {roundIndex}";
    }

    string StatusLine()
    {
        int playerDamage = playerBaseDamage + playerDamagePerLevel * (level - 1);
        return $"PlayerLevel={level} | Damage={playerDamage} | XP={xp}/{xpToNext} | Wins={wins} | Round={roundIndex}";
    }

    void LogLine(string s)
    {
        // Behåll senaste raderna för läsbarhet
        const int maxChars = 900;
        logText.text = (logText.text + "\n" + s).Trim();
        if (logText.text.Length > maxChars)
            logText.text = logText.text.Substring(logText.text.Length - maxChars);
    }

    void ClearLog() => logText.text = "";

    // ---------- Minimal UI factory ----------
    RectTransform CreatePanel(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax,
        Vector2 offsetMin = default, Vector2 offsetMax = default)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.offsetMin = offsetMin;
        rt.offsetMax = offsetMax;
        return rt;
    }

    Text CreateText(Transform parent, string name, string text, int fontSize, TextAnchor alignment,
        Vector2 anchorMin, Vector2 anchorMax)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Text));
        go.transform.SetParent(parent, false);

        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        var t = go.GetComponent<Text>();
        t.text = text;
        t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        t.fontSize = fontSize;
        t.alignment = alignment;
        t.color = Color.white;
        t.horizontalOverflow = HorizontalWrapMode.Wrap;
        t.verticalOverflow = VerticalWrapMode.Overflow;

        return t;
    }

    Slider CreateHPBar(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Slider));
        go.transform.SetParent(parent, false);

        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        var slider = go.GetComponent<Slider>();
        slider.minValue = 0;
        slider.maxValue = 1;
        slider.value = 1;
        slider.direction = Slider.Direction.LeftToRight;
        slider.transition = Selectable.Transition.None;

        // Background
        var bg = new GameObject("Background", typeof(RectTransform), typeof(Image));
        bg.transform.SetParent(go.transform, false);
        var bgRT = bg.GetComponent<RectTransform>();
        bgRT.anchorMin = new Vector2(0, 0);
        bgRT.anchorMax = new Vector2(1, 1);
        bgRT.offsetMin = Vector2.zero;
        bgRT.offsetMax = Vector2.zero;
        bg.GetComponent<Image>().color = new Color(0.2f, 0.2f, 0.2f, 1f);

        // Fill area
        var fillArea = new GameObject("Fill Area", typeof(RectTransform));
        fillArea.transform.SetParent(go.transform, false);
        var faRT = fillArea.GetComponent<RectTransform>();
        faRT.anchorMin = new Vector2(0, 0);
        faRT.anchorMax = new Vector2(1, 1);
        faRT.offsetMin = new Vector2(5, 5);
        faRT.offsetMax = new Vector2(-5, -5);

        var fill = new GameObject("Fill", typeof(RectTransform), typeof(Image));
        fill.transform.SetParent(fillArea.transform, false);
        var fillRT = fill.GetComponent<RectTransform>();
        fillRT.anchorMin = new Vector2(0, 0);
        fillRT.anchorMax = new Vector2(1, 1);
        fillRT.offsetMin = Vector2.zero;
        fillRT.offsetMax = Vector2.zero;
        fill.GetComponent<Image>().color = new Color(0.25f, 0.85f, 0.35f, 1f);

        // Handle (not used, but Slider wants one sometimes)
        var handle = new GameObject("Handle", typeof(RectTransform), typeof(Image));
        handle.transform.SetParent(go.transform, false);
        handle.GetComponent<Image>().color = new Color(1, 1, 1, 0);

        slider.targetGraphic = fill.GetComponent<Image>();
        slider.fillRect = fillRT;
        slider.handleRect = handle.GetComponent<RectTransform>();

        return slider;
    }

    Image CreateBox(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);

        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        var img = go.GetComponent<Image>();
        img.color = Color.gray;
        return img;
    }

    Button CreateButton(Transform parent, string name, string label, Vector2 anchorMin, Vector2 anchorMax, UnityEngine.Events.UnityAction onClick)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);

        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        var img = go.GetComponent<Image>();
        img.color = new Color(0.18f, 0.18f, 0.22f, 1f);

        var btn = go.GetComponent<Button>();
        btn.onClick.AddListener(onClick);

        var text = CreateText(go.transform, "Label", label, 18, TextAnchor.MiddleCenter,
            new Vector2(0, 0), new Vector2(1, 1));
        text.color = Color.white;

        return btn;
    }
    void SetRoundSummary(string s)
    {
        // Kort, alltid läsbart. Ersätter tidigare innehåll.
        logText.text = s;
    }

    void AppendToCombat(string s)
    {
        // Om du vill stapla 2–3 rader inom samma turn.
        // Annars kan du strunta i denna och bara sätta combatText direkt.
        combatText.text = (combatText.text + "\n" + s).Trim();
    }
    void BuildLevelUpPopup(Transform parent)
    {
        // Mörk overlay
        levelUpPanel = CreatePanel(parent, "LevelUpPopup", new Vector2(0, 0), new Vector2(1, 1));
        levelUpPanel.GetComponent<Image>().color = new Color(0, 0, 0, 0.65f);

        // Inner box
        var box = CreatePanel(levelUpPanel, "Box", new Vector2(0.30f, 0.25f), new Vector2(0.70f, 0.75f));
        box.GetComponent<Image>().color = new Color(0.14f, 0.14f, 0.17f, 1f);

        levelUpTitleText = CreateText(box.transform, "Title", "LEVEL UP!", 28, TextAnchor.MiddleCenter,
            new Vector2(0.05f, 0.78f), new Vector2(0.95f, 0.95f));

        levelUpBodyText = CreateText(box.transform, "Body", "", 18, TextAnchor.UpperLeft,
            new Vector2(0.07f, 0.25f), new Vector2(0.93f, 0.78f));

        levelUpOkButton = CreateButton(box.transform, "OkButton", "OK",
            new Vector2(0.35f, 0.07f), new Vector2(0.65f, 0.20f), OnLevelUpOkClicked);
    }
    void ShowLevelUpPopup(string title, string body)
    {
        levelUpPopupOpen = true;
        levelUpPanel.gameObject.SetActive(true);

        levelUpTitleText.text = title;
        levelUpBodyText.text = body;

        // Pausa inputs tills OK
        attackButton.interactable = false;
        nextRoundButton.interactable = false;
    }

    void HideLevelUpPopup()
    {
        levelUpPopupOpen = false;
        if (levelUpPanel != null) levelUpPanel.gameObject.SetActive(false);
    }

    void OnLevelUpOkClicked()
    {
        HideLevelUpPopup();

        // Efter levelup: om rundan redan är slut (vinst), tillåt Next Round.
        // (Rimligt: man levlar upp efter en vinst.)
        if (!roundActive)
            nextRoundButton.interactable = true;

        restartButton.interactable = true;
    }
    int RollEnemyDamage(int eLevel, out bool didCrit)
    {
        float baseDmg = enemyBaseDamage + enemyDamagePerLevel * eLevel;

        float min = baseDmg * enemyMinMultiplier;
        float max = baseDmg * enemyMaxMultiplier;

        float t = Biased01(enemyDamageBias);
        float dmg = Mathf.Lerp(min, max, t);

        didCrit = Random.value < enemyCritChance;
        if (didCrit)
            dmg *= enemyCritMultiplier;

        return Mathf.Max(1, Mathf.CeilToInt(dmg));
    }

    float Biased01(float bias)
    {
        // bias = antal samples som medelvärdesbildas (1 = uniform, 3 = ganska stabilt)
        int n = Mathf.Clamp(Mathf.RoundToInt(bias), 1, 12);
        float sum = 0f;
        for (int i = 0; i < n; i++)
            sum += Random.value;
        return sum / n;
    }
}