namespace ConectaRevit.Addin.Licensing;

// Valida e cacheia a licença do produto (ARCHITECTURE § 5.7).
// TODO Fase 5: implementar chamada ao endpoint Supabase e grace period offline.
//
// Fluxo:
//   Conectar → POST /functions/v1/validate-license { key, machineId, productVersion }
//            ← { valid, plan, expiresAt }
//   machineId = hash estável de identificador de hardware (enforcement de seat).
//   Cacheia resultado; grace period offline de 7 dias.
//   Chave inválida/expirada → botão Conectar bloqueado com mensagem.
internal sealed class LicenseManager
{
    // TODO Fase 5: implementar ValidateAsync(string key), GetMachineId(), CacheResult().
    public bool IsValid { get; private set; }
}
