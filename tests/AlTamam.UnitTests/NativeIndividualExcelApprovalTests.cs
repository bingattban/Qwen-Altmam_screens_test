using AlTamam.Domain.Enums;
using AlTamam.NativePresentation;
using Xunit;

namespace AlTamam.UnitTests;

public sealed class NativeIndividualExcelApprovalTests
{
    private static readonly NativeIdentity Actor = new("USR-1","admin",
        UserRole.GeneralAdministration,"NONCE-1");
    private static readonly NativeIndividualExcelRequest Request = new("PER-001","","",0,"","");
    private const string Fingerprint = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    [Fact]
    public void Approval_is_bound_to_exact_session_scope_and_is_single_use()
    {
        var gate = new NativeIndividualExcelApprovalGate();
        var token = gate.Publish(gate.Begin(),Actor,Request,Fingerprint);
        Assert.Equal(Fingerprint,gate.Consume(Actor,Request,token));
        Assert.Throws<UnauthorizedAccessException>(()=>gate.Consume(Actor,Request,token));
    }

    [Theory]
    [InlineData("session")]
    [InlineData("person")]
    [InlineData("filter")]
    [InlineData("token")]
    public void Altered_scope_never_authorizes_export(string change)
    {
        var gate = new NativeIndividualExcelApprovalGate();
        var token = gate.Publish(gate.Begin(),Actor,Request,Fingerprint);
        var actor = change=="session" ? Actor with { SessionNonce="OTHER" } : Actor;
        var request = change=="person" ? Request with { PersonId="PER-002" } :
            change=="filter" ? Request with { StatusId="STS-1" } : Request;
        Assert.Throws<UnauthorizedAccessException>(()=>gate.Consume(actor,request,
            change=="token" ? "wrong" : token));
        Assert.Throws<UnauthorizedAccessException>(()=>gate.Consume(Actor,Request,token));
    }

    [Fact]
    public void New_preview_or_cancel_invalidates_pending_and_previous_approval()
    {
        var gate = new NativeIndividualExcelApprovalGate();
        var first = gate.Begin();
        var token = gate.Publish(first,Actor,Request,Fingerprint);
        var next = gate.Begin();
        Assert.Throws<UnauthorizedAccessException>(()=>gate.Publish(first,Actor,Request,Fingerprint));
        Assert.Throws<UnauthorizedAccessException>(()=>gate.Consume(Actor,Request,token));
        var latest = gate.Publish(next,Actor,Request,Fingerprint);
        gate.Invalidate();
        Assert.Throws<UnauthorizedAccessException>(()=>gate.Consume(Actor,Request,latest));
    }

    [Fact]
    public void Expiry_and_concurrent_confirmation_fail_closed()
    {
        var clock = new Clock();
        var gate = new NativeIndividualExcelApprovalGate(clock);
        var token = gate.Publish(gate.Begin(),Actor,Request,Fingerprint);
        clock.Now = clock.Now.AddMinutes(2);
        Assert.Throws<UnauthorizedAccessException>(()=>gate.Consume(Actor,Request,token));
        token = gate.Publish(gate.Begin(),Actor,Request,Fingerprint);
        int accepted=0;
        Parallel.For(0, 16, _ =>
        {
            try { gate.Consume(Actor,Request,token); Interlocked.Increment(ref accepted); }
            catch(UnauthorizedAccessException) { }
        });
        Assert.Equal(1,accepted);
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = new(2026,11,25,0,0,0,TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow()=>Now;
    }
}
