namespace FPSOverlay
{
    /// <summary>System/user prompt templates for the AI OC Assistant backend. prompt crafting ✨</summary>
    public static class AiOcPromptTemplate
    {
        public const string SystemPrompt = """
You are the Mars FPS Monitor AI Overclock Assistant.
Your ONLY job is to propose extremely safe, conservative GPU overclock profiles
for three modes: Eco, Performance, Extreme.

RULES (non-negotiable):
1. Prefer under-clocking ambition. Stability and thermals beat benchmarks.
2. Never exceed the client constraints object (max_core_offset_mhz, max_memory_offset_mhz, power limits).
3. Soft guidance even below those ceilings:
   - Eco: core 0, memory 0, power_limit_percent null or ≤100
   - Performance: core ≤ +50, memory ≤ +100, power ≤ 105
   - Extreme: core ≤ +100, memory ≤ +300, power ≤ 110 — still conservative for the given GPU generation
4. Laptop / low-TDP / hot idle GPUs → bias further downward.
5. Hot current_temp_c (≥80) or hotspot (≥90) → reduce Extreme/Performance offsets.
6. Temperature bands must be contiguous and cover roughly 0–100°C without large gaps:
   Extreme = cooler band, Performance = mid, Eco = hot band (thermal safety).
7. Respond with JSON ONLY matching the response schema. No markdown fences. No commentary outside JSON.
8. power_limit_percent may be null to leave stock power limit untouched (preferred when unsure).
9. Never suggest negative core/memory offsets in this assistant.
10. Include a short rationale per recommendation (one sentence).
""";

        public const string UserPromptFormat = """
Propose Eco, Performance, and Extreme GPU OC profiles for this hardware.

REQUEST_JSON:
{0}

Return a single JSON object:
{{
  "schema_version": 1,
  "model": "<your model id>",
  "notes": "<optional overall note>",
  "warnings": [],
  "recommendations": [
    {{
      "mode": "Eco",
      "profile_name": "AI Eco",
      "min_temp": 82,
      "max_temp": 100,
      "core_offset_mhz": 0,
      "memory_offset_mhz": 0,
      "power_limit_percent": null,
      "rationale": "..."
    }},
    {{
      "mode": "Performance",
      "profile_name": "AI Performance",
      "min_temp": 75,
      "max_temp": 81,
      "core_offset_mhz": 25,
      "memory_offset_mhz": 50,
      "power_limit_percent": null,
      "rationale": "..."
    }},
    {{
      "mode": "Extreme",
      "profile_name": "AI Extreme",
      "min_temp": 0,
      "max_temp": 74,
      "core_offset_mhz": 50,
      "memory_offset_mhz": 100,
      "power_limit_percent": null,
      "rationale": "..."
    }}
  ]
}}
""";

        public static string BuildUserPrompt(string requestJson)
            => string.Format(UserPromptFormat, requestJson);

        /// <summary>OpenAI-style chat messages body helper for backend adapters. envelope time.</summary>
        public static object BuildChatMessages(string requestJson) => new
        {
            messages = new object[]
            {
                new { role = "system", content = SystemPrompt },
                new { role = "user", content = BuildUserPrompt(requestJson) }
            },
            response_format = new { type = "json_object" },
            temperature = 0.2
        };
    }
}
