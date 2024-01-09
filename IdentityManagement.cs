namespace SamedisStaffSync {
  public class CurrentUser {
    public record Attributes(
      string id,
      string actor_id,
      bool? active,
      string email,
      string first_name,
      string last_name,
      int? gender,
      object locale,
      object @short,
      object title,
      object mobile,
      object company,
      object office,
      object department,
      object personnel_number,
      object job_title,
      string username,
      Image image,
      IReadOnlyList<string> candos,
      IReadOnlyList<Tenant> tenants,
      object recent_invite_tokens,
      bool? otp_enabled,
      bool? otp_provided,
      object otp_provisioning_qr_code,
      object otp_secret_key,
      object otp_backup_codes
    );

    public record Data(
      string id,
      string type,
      Attributes attributes
    );

    public record Image(
      string large,
      string medium,
      string small
    );

    public record Meta(
      Msg msg,
      string token,
      string refresh_token,
      int? expires_in,
      string redirect_url,
      string app,
      IReadOnlyList<string> check_acceptances
    );

    public record Msg(
      bool? success
    );

    public record Root(
      Data data,
      Meta meta
    );

    public record Tenant(
      string id,
      string name,
      string short_name,
      string full_name,
      string title,
      IReadOnlyList<object> enterprises,
      IReadOnlyList<string> candos,
      Image image
    );
  }
}