# RubiKit API Documentation

Complete reference for building AI-enhanced modules for RubiKit OS.

## Table of Contents

1. [Game State API](#game-state-api)
2. [AI Service API](#ai-service-api)
3. [Event System](#event-system)
4. [Module Integration](#module-integration)
5. [Examples](#examples)

---

## Game State API

Access real-time game data from the Anarchy Online client.

### HTTP Endpoints

Base URL: `http://127.0.0.1:8777`

#### GET `/api/state`
Returns complete current game state.

**Response:**
```json
{
  "hp": {
    "now": 5432,
    "max": 6500,
    "pct": 84
  },
  "nano": {
    "now": 3210,
    "max": 4000,
    "pct": 80
  },
  "core": {
    "AddAllOff": 420,
    "AddAllDef": 380,
    "CriticalIncrease": 650,
    "XPModifier": 200
  },
  "dmg": {
    "MeleeDamageModifier": 150,
    "ProjectileDamageModifier": 200,
    "EnergyDamageModifier": 175,
    ...
  },
  "ac": {
    "MeleeAC": 1200,
    "ProjectileAC": 1100,
    "EnergyAC": 950,
    ...
  },
  "all": {
    "Strength": 450,
    "Agility": 520,
    ...
  },
  "pins": [
    {"name": "NanoCInit", "v": 1250, "label": "Nano Init"}
  ],
  "settings": {
    "theme": "theme-aetherium",
    "compact": "false",
    ...
  }
}
```

#### GET `/api/groups`
Returns stat groupings and categories.

#### GET `/events` (Server-Sent Events)
Real-time stream of game state updates.

```javascript
const eventSource = new EventSource('http://127.0.0.1:8777/events');
eventSource.addEventListener('message', (event) => {
  const gameState = JSON.parse(event.data);
  console.log('Game state updated:', gameState);
});
```

#### POST `/api/cmd`
Send commands to the game client.

**Parameters:**
- `action`: Command type (pin_add, pin_remove, theme, etc.)
- `value`: Command value

**Example:**
```javascript
fetch('http://127.0.0.1:8777/api/cmd?action=pin_add&value=Strength');
```

---

## AI Service API

Access AI-powered analysis and suggestions using the local LLM.

### Initialization

The AI service is available globally as `window.rubiAI`.

```javascript
// Check if AI is available
if (window.rubiAI && window.rubiAI.config.enabled) {
  const api = window.rubiAI.getAPI();
  // Use the API
}
```

### API Methods

#### `getGameState()`
Returns current game state object.

```javascript
const api = window.rubiAI.getAPI();
const state = api.getGameState();
console.log('HP:', state.hp.pct + '%');
```

#### `getContext()`
Returns formatted context array for current game state.

```javascript
const context = api.getContext();
// ["HP: 5432/6500 (84%)", "Nano: 3210/4000 (80%)", ...]
```

#### `subscribe(eventType, handler)`
Subscribe to real-time events.

**Event Types:**
- `'stateUpdate'` - Game state changed
- `'change'` - Significant change detected
- `'contextUpdate'` - Context string updated

```javascript
const unsubscribe = api.subscribe('change', (change) => {
  console.log('Change detected:', change);
  if (change.type === 'health_low') {
    alert('Health is low!');
  }
});

// Later: unsubscribe()
```

#### `query(prompt, options)`
Send a custom query to the LLM.

```javascript
const response = await api.query(
  'Should I focus on melee or ranged combat?',
  {
    max_tokens: 150,
    temperature: 0.7
  }
);
console.log('AI says:', response);
```

#### `analyzeStats(stats)`
Get AI analysis of stat distribution.

```javascript
const analysis = await api.analyzeStats({
  Strength: 450,
  Agility: 520,
  Stamina: 480
});
```

#### `analyzeCombat(combatLog)`
Analyze combat log for tactical suggestions.

```javascript
const suggestions = await api.analyzeCombat(
  'Hit for 450 damage. Enemy used Chemical damage. Took 280 damage.'
);
```

#### `suggestLoadout(profession, level)`
Get equipment suggestions.

```javascript
const loadout = await api.suggestLoadout('Enforcer', 220);
```

#### `explainStat(statName, currentValue)`
Get explanation of a stat and how to improve it.

```javascript
const explanation = await api.explainStat('NanoCInit', 1250);
```

#### `suggestArmorType(damageType)`
Get armor prioritization advice.

```javascript
const advice = await api.suggestArmorType('Chemical');
```

#### `onCallout(handler)`
Register for AI-generated callouts.

```javascript
api.onCallout((callout) => {
  console.log('[AI]', callout.message);
  showNotification(callout.message, callout.severity);
});
```

---

## Event System

### Change Events

The AI service automatically detects and reports significant changes:

```javascript
api.subscribe('change', (change) => {
  console.log('Type:', change.type);
  console.log('Severity:', change.severity); // 'info', 'warning', 'critical'
  console.log('Data:', change.data);
});
```

**Change Types:**
- `health_low` - Health below 30%
- `health_medium` - Health below 50%
- `nano_low` - Nano below 20%
- `stat_change` - Any stat value changed

### Callouts

AI-generated contextual messages:

```javascript
api.onCallout((callout) => {
  /*
  {
    type: 'health_low',
    severity: 'warning',
    message: 'Your health is critical. Consider using a health stim...',
    timestamp: 1234567890,
    change: {...}
  }
  */
});
```

---

## Module Integration

### Basic Integration

```html
<!DOCTYPE html>
<html>
<head>
  <title>My Module</title>
</head>
<body>
  <div id="stats"></div>
  <div id="ai-suggestions"></div>

  <script>
    // Wait for AI service to load
    window.addEventListener('load', async () => {
      if (!window.rubiAI) {
        console.warn('AI service not available');
        return;
      }

      const api = window.rubiAI.getAPI();

      // Subscribe to game state
      api.subscribe('stateUpdate', (state) => {
        updateDisplay(state);
      });

      // Listen for AI callouts
      api.onCallout((callout) => {
        showAISuggestion(callout.message);
      });

      // Request initial analysis
      const state = api.getGameState();
      if (state.core) {
        const analysis = await api.analyzeStats(state.core);
        showAISuggestion(analysis);
      }
    });

    function updateDisplay(state) {
      document.getElementById('stats').innerHTML = `
        <p>HP: ${state.hp?.pct || 0}%</p>
        <p>Nano: ${state.nano?.pct || 0}%</p>
      `;
    }

    function showAISuggestion(message) {
      const div = document.getElementById('ai-suggestions');
      div.innerHTML = `<p><strong>AI:</strong> ${message}</p>`;
    }
  </script>
</body>
</html>
```

### Advanced Integration

```javascript
class AIEnhancedModule {
  constructor() {
    this.api = null;
    this.subscriptions = [];
  }

  async initialize() {
    if (!window.rubiAI) {
      throw new Error('AI service required');
    }

    this.api = window.rubiAI.getAPI();

    // Subscribe to events
    this.subscriptions.push(
      this.api.subscribe('change', (change) => this.handleChange(change))
    );

    this.subscriptions.push(
      this.api.subscribe('contextUpdate', (ctx) => this.handleContext(ctx))
    );

    // Register callout handler
    this.api.onCallout((callout) => this.handleCallout(callout));
  }

  async handleChange(change) {
    if (change.severity === 'warning') {
      // Get AI advice for this change
      const advice = await this.api.query(
        `How should I respond to: ${change.type}?`
      );
      this.displayAdvice(advice);
    }
  }

  handleContext(context) {
    console.log('Context updated:', context);
  }

  handleCallout(callout) {
    this.showNotification(callout.message, callout.severity);
  }

  displayAdvice(text) {
    // Your UI update logic
  }

  showNotification(message, severity) {
    // Your notification logic
  }

  destroy() {
    // Unsubscribe from all events
    this.subscriptions.forEach(unsub => unsub());
  }
}

// Usage
const module = new AIEnhancedModule();
module.initialize();
```

---

## Examples

### Example 1: Health Monitor with AI Advice

```javascript
const api = window.rubiAI.getAPI();

api.subscribe('change', async (change) => {
  if (change.type === 'health_low') {
    const advice = await api.query(
      `My health is at ${change.data.pct}%. What should I do right now?`,
      { max_tokens: 50 }
    );
    alert(`AI Advice: ${advice}`);
  }
});
```

### Example 2: Stat Analyzer

```javascript
async function analyzeMyStats() {
  const api = window.rubiAI.getAPI();
  const state = api.getGameState();

  if (state.all) {
    const analysis = await api.analyzeStats({
      Strength: state.all.Strength,
      Agility: state.all.Agility,
      Stamina: state.all.Stamina,
      Intelligence: state.all.Intelligence
    });

    console.log('AI Analysis:', analysis);
  }
}
```

### Example 3: Armor Class Assistant

```javascript
async function checkArmorForDamageType(damageType) {
  const api = window.rubiAI.getAPI();
  const state = api.getGameState();

  // Get current AC for this damage type
  const acKey = `${damageType}AC`;
  const currentAC = state.ac?.[acKey] || 0;

  // Ask AI for advice
  const advice = await api.suggestArmorType(damageType);

  return {
    damageType,
    currentAC,
    advice
  };
}

// Usage
const result = await checkArmorForDamageType('Chemical');
console.log(result.advice);
```

### Example 4: Real-time Combat Assistant

```javascript
class CombatAssistant {
  constructor() {
    this.api = window.rubiAI.getAPI();
    this.combatLog = [];
  }

  logCombatEvent(event) {
    this.combatLog.push(event);

    // Analyze every 5 events
    if (this.combatLog.length >= 5) {
      this.analyzeRecentCombat();
      this.combatLog = [];
    }
  }

  async analyzeRecentCombat() {
    const logText = this.combatLog.join('\n');
    const analysis = await this.api.analyzeCombat(logText);
    this.displaySuggestion(analysis);
  }

  displaySuggestion(text) {
    // Show in UI
    console.log('[Combat AI]', text);
  }
}
```

---

## Configuration

### AI Service Configuration

```javascript
// Access current config
const config = window.rubiAI.config;

// Check if enabled
if (config.enabled) {
  console.log('AI is active');
  console.log('LLM endpoint:', config.llmEndpoint);
  console.log('Temperature:', config.temperature);
}
```

### Module Configuration

When creating your module, you can check for AI availability:

```javascript
function initializeModule() {
  const hasAI = window.rubiAI && window.rubiAI.config.enabled;

  if (hasAI) {
    // Enable AI features
    enableAIFeatures();
  } else {
    // Fallback to basic features
    console.warn('AI features disabled');
  }
}
```

---

## Best Practices

1. **Always check for AI availability** before using AI features
2. **Handle errors gracefully** - LLM may be offline
3. **Use appropriate token limits** - Keep responses concise
4. **Subscribe to events** instead of polling
5. **Unsubscribe when done** to prevent memory leaks
6. **Cache AI responses** when appropriate
7. **Provide fallback UI** for non-AI mode
8. **Rate limit AI queries** to avoid overwhelming the LLM

---

## Troubleshooting

### AI Service Not Available

```javascript
if (!window.rubiAI) {
  console.error('AI service not loaded. Check if rubikit-ai.js is included.');
}
```

### LLM Connection Failed

```javascript
const api = window.rubiAI.getAPI();
try {
  await api.query('test');
} catch (error) {
  console.error('LLM unavailable:', error);
  // Fall back to non-AI features
}
```

### Event Source Errors

```javascript
api.subscribe('stateUpdate', (state) => {
  if (!state || !state.hp) {
    console.warn('Invalid game state received');
    return;
  }
  // Process valid state
});
```

---

## Future Expansion

The API is designed to be infinitely expandable. Future additions may include:

- Voice command integration
- Image recognition for game screenshots
- Automated trading analysis
- Quest optimization
- Team coordination AI
- Custom AI personalities
- Multi-agent AI systems

To stay updated, check the repository for new API endpoints and features.

---

**Last Updated:** 2025-12-12
**Version:** 2.1
**Author:** RubiKit Development Team
