// validate-license — ConectaRevit Edge Function
//
// Recebe POST { license_key, machine_id, product_version }
// Consulta a tabela `licenses` via SERVICE_ROLE (bypassa RLS).
// Retorna { valid, plan?, expires_at?, signature? } em sucesso
//       ou { valid: false, reason }                em falha.
//
// Envs obrigatórias (injetadas pelo Supabase — NUNCA hardcodar):
//   SUPABASE_URL                — automática
//   SUPABASE_SERVICE_ROLE_KEY   — automática
//   LICENSE_SIGNING_SECRET      — configure em Project → Edge Functions → Secrets
//
// Esquema HMAC (documentado para o add-in):
//   sign_data  = "{license_key}|{machine_id}|{expires_at}"
//              onde expires_at é a string ISO raw do banco, ou "" se null.
//   signature  = HMAC-SHA256(sign_data, LICENSE_SIGNING_SECRET) em hex minúsculo
//   O add-in recomputa com o mesmo segredo embutido e compara em tempo constante.

import { createClient } from "jsr:@supabase/supabase-js@2";

const CORS_HEADERS = {
  "Access-Control-Allow-Origin":  "*",
  "Access-Control-Allow-Headers": "authorization, x-client-info, apikey, content-type",
};

function json(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { ...CORS_HEADERS, "Content-Type": "application/json" },
  });
}

async function hmacHex(secret: string, data: string): Promise<string> {
  const key = await crypto.subtle.importKey(
    "raw",
    new TextEncoder().encode(secret),
    { name: "HMAC", hash: "SHA-256" },
    false,
    ["sign"],
  );
  const sig = await crypto.subtle.sign(
    "HMAC",
    key,
    new TextEncoder().encode(data),
  );
  return Array.from(new Uint8Array(sig))
    .map((b) => b.toString(16).padStart(2, "0"))
    .join("");
}

Deno.serve(async (req) => {
  // Preflight CORS
  if (req.method === "OPTIONS") {
    return new Response("ok", { headers: CORS_HEADERS });
  }

  if (req.method !== "POST") {
    return json({ valid: false, reason: "method_not_allowed" }, 405);
  }

  // ── Parse body ────────────────────────────────────────────────────────────
  let licenseKey: string;
  let machineId: string;
  let productVersion: string | undefined;

  try {
    const body = await req.json() as {
      license_key?: unknown;
      machine_id?: unknown;
      product_version?: unknown;
    };
    licenseKey     = String(body.license_key  ?? "").trim();
    machineId      = String(body.machine_id   ?? "").trim();
    productVersion = body.product_version != null ? String(body.product_version) : undefined;
  } catch {
    return json({ valid: false, reason: "invalid_json" }, 400);
  }

  if (!licenseKey || !machineId) {
    return json({ valid: false, reason: "missing_params" }, 400);
  }

  // ── Supabase client (service role — bypassa RLS) ──────────────────────────
  const supabase = createClient(
    Deno.env.get("SUPABASE_URL")!,
    Deno.env.get("SUPABASE_SERVICE_ROLE_KEY")!,
    { auth: { persistSession: false } },
  );

  try {
    // ── Busca a chave ─────────────────────────────────────────────────────
    const { data: license, error } = await supabase
      .from("licenses")
      .select("license_key, status, plan, expires_at, machine_id, activated_at")
      .eq("license_key", licenseKey)
      .maybeSingle();

    if (error) {
      console.error("validate-license: db error:", error.message);
      return json({ valid: false, reason: "server_error" }, 500);
    }

    // a) não encontrada
    if (!license) {
      return json({ valid: false, reason: "not_found" });
    }

    // b) suspensa / inativa
    if (license.status !== "active") {
      return json({ valid: false, reason: "suspended" });
    }

    // c) expirada
    const now = new Date();
    if (license.expires_at && new Date(license.expires_at) < now) {
      return json({ valid: false, reason: "expired" });
    }

    // d/e) machine binding
    if (!license.machine_id) {
      // Primeira ativação — grava machine_id
      const { error: updErr } = await supabase
        .from("licenses")
        .update({
          machine_id:   machineId,
          activated_at: now.toISOString(),
        })
        .eq("license_key", licenseKey);

      if (updErr) {
        console.error("validate-license: update error:", updErr.message);
        return json({ valid: false, reason: "activation_error" }, 500);
      }
    } else if (license.machine_id !== machineId) {
      // f) machine diferente
      return json({ valid: false, reason: "machine_mismatch" });
    }

    // ── Assinatura HMAC ───────────────────────────────────────────────────
    const signingSecret = Deno.env.get("LICENSE_SIGNING_SECRET") ?? "";
    const expiresAtRaw  = license.expires_at ?? "";
    const signData      = `${licenseKey}|${machineId}|${expiresAtRaw}`;
    const signingSecret = Deno.env.get("LICENSE_SIGNING_SECRET") ?? "";
	if (!signingSecret) {
		console.error("validate-license: LICENSE_SIGNING_SECRET não configurado");
		return json({ valid: false, reason: "server_misconfigured" }, 500);
	}
	const expiresAtRaw = license.expires_at ?? "";
	const signData     = `${licenseKey}|${machineId}|${expiresAtRaw}`;
	const signature    = await hmacHex(signingSecret, signData);

    console.log(
      `validate-license: OK key=***${licenseKey.slice(-4)} ` +
      `machine=***${machineId.slice(-6)} version=${productVersion ?? "?"} ` +
      `plan=${license.plan ?? "standard"} expires=${expiresAtRaw || "never"}`,
    );

    return json({
      valid:      true,
      plan:       license.plan ?? "standard",
      expires_at: license.expires_at,
      signature,
    });
  } catch (err) {
    console.error("validate-license: unexpected error:", err);
    return json({ valid: false, reason: "server_error" }, 500);
  }
});
