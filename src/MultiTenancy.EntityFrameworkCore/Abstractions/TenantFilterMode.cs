namespace MultiTenancy.Abstractions;

/// <summary>
/// Chooses what the global tenant query filter does when there is NO ambient tenant
/// (<see cref="ICurrentTenantService.TenantId"/> is <c>null</c>).
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>There is no default, deliberately.</b> Every caller must state which behaviour it wants,
/// because the two modes differ by one character at the call site and by "every tenant's rows" at
/// runtime. A default would let a service acquire a polarity nobody chose.
/// </para>
/// <para>
/// The services in this fleet do NOT agree on the answer, and that disagreement is informed rather
/// than accidental — see the per-member docs. This enum exists so the shared helper can serve both
/// camps without silently converting anyone.
/// </para>
/// </remarks>
public enum TenantFilterMode
{
  /// <summary>
  /// No ambient tenant matches EVERY row (<c>TenantId == null || e.TenantId == TenantId</c>).
  /// </summary>
  /// <remarks>
  /// <para>
  /// ⚠️ <b>This is a PASS-THROUGH, not isolation.</b> A caller with no ambient tenant reads across
  /// every tenant. That is relied upon by migrations, background jobs, MassTransit consumers and
  /// unauthenticated webhook/reset paths, which have no <c>HttpContext</c> and therefore no ambient
  /// tenant — but it means the filter gives those callers no protection whatsoever.
  /// </para>
  /// <para>
  /// <b>Any query on such a path MUST carry an explicit <c>TenantId</c> predicate.</b> The predicate
  /// is the isolation; the filter is not. See <c>Agora.IOrderRepository.GetForWebhookAsync</c> and
  /// <c>Content.EfContentRepository.GetListByUserIdAsync</c> (whose GDPR consumers run off a
  /// RabbitMQ delivery — ambient tenant always null, filter always matching everything).
  /// </para>
  /// </remarks>
  FailOpen,

  /// <summary>
  /// No ambient tenant matches NO rows (<c>TenantId != null &amp;&amp; e.TenantId == TenantId</c>).
  /// </summary>
  /// <remarks>
  /// <para>
  /// The two failure modes are not symmetric. <see cref="FailOpen"/> leaks data silently and
  /// correct-looking code is the symptom. <see cref="FailClosed"/> turns the same mistake into an
  /// immediate, obvious "not found" — a functional bug fixed in ten minutes in development, instead
  /// of a breach found in an audit.
  /// </para>
  /// <para>
  /// ⚠️ <b>Consequence:</b> a caller with no ambient tenant reads nothing at all. Such a caller must
  /// name its tenant explicitly AND bypass the filter — <c>IgnoreQueryFilters()</c> paired with
  /// <c>e.TenantId == tenantId</c>. That combination is load-bearing: drop the predicate and the
  /// query reads across every tenant. See <c>Zygos.IPaymentInstructionRepository</c>.
  /// </para>
  /// </remarks>
  FailClosed,
}
