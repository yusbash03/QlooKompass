using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace KultureCompass.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "business_profiles",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    brand_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    category = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    category_other = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    products_description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    target_market = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    primary_location = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    marketing_objective = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    objective_notes = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    brand_identity = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    reference_entities = table.Column<string>(type: "jsonb", nullable: false),
                    budget_range = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_business_profiles", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "evaluation_runs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    business_profile_id = table.Column<Guid>(type: "uuid", nullable: false),
                    grounded_plan_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    llm_only_output = table.Column<string>(type: "jsonb", nullable: true),
                    grounded_metrics = table.Column<string>(type: "jsonb", nullable: true),
                    llm_only_metrics = table.Column<string>(type: "jsonb", nullable: true),
                    notes = table.Column<string>(type: "jsonb", nullable: false),
                    error_code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    error_message = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_evaluation_runs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "growth_plans",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    business_profile_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    stage = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    idempotency_key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    request_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    brief_snapshot = table.Column<string>(type: "jsonb", nullable: false),
                    options = table.Column<string>(type: "jsonb", nullable: false),
                    interpretation = table.Column<string>(type: "jsonb", nullable: true),
                    research_plan = table.Column<string>(type: "jsonb", nullable: true),
                    content_summary = table.Column<string>(type: "jsonb", nullable: true),
                    schema_version = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    prompt_version = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    qloo_mode = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    llm_provider = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    llm_model = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    is_mock = table.Column<bool>(type: "boolean", nullable: false),
                    qloo_call_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    llm_call_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    llm_usage = table.Column<string>(type: "jsonb", nullable: true),
                    error_code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    error_message = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    stage_log = table.Column<string>(type: "jsonb", nullable: false),
                    content_limitations = table.Column<string>(type: "jsonb", nullable: false),
                    content_unlinked_mentions = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_growth_plans", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    display_name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    user_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    normalized_user_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    normalized_email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    email_confirmed = table.Column<bool>(type: "boolean", nullable: false),
                    password_hash = table.Column<string>(type: "text", nullable: true),
                    security_stamp = table.Column<string>(type: "text", nullable: true),
                    concurrency_stamp = table.Column<string>(type: "text", nullable: true),
                    phone_number = table.Column<string>(type: "text", nullable: true),
                    phone_number_confirmed = table.Column<bool>(type: "boolean", nullable: false),
                    two_factor_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    lockout_end = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    lockout_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    access_failed_count = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_users", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "growth_plan_evidence",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    evidence_key = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    qloo_entity_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    entity_type = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    subtype = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    source_operation = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    source_signals = table.Column<string>(type: "jsonb", nullable: false),
                    affinity = table.Column<double>(type: "numeric(6,5)", nullable: true),
                    popularity = table.Column<double>(type: "numeric(6,5)", nullable: true),
                    tags = table.Column<string>(type: "jsonb", nullable: false),
                    metadata = table.Column<string>(type: "jsonb", nullable: false),
                    quality = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    quality_reasons = table.Column<string>(type: "jsonb", nullable: false),
                    appeared_in_operations = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    retrieved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    growth_plan_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_growth_plan_evidence", x => x.id);
                    table.ForeignKey(
                        name: "fk_growth_plan_evidence_growth_plans_growth_plan_id",
                        column: x => x.growth_plan_id,
                        principalTable: "growth_plans",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "growth_plan_recommendations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    rec_id = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ordinal = table.Column<int>(type: "integer", nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    evidence_keys = table.Column<string[]>(type: "text[]", nullable: false),
                    evidence_strength = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    growth_plan_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_growth_plan_recommendations", x => x.id);
                    table.ForeignKey(
                        name: "fk_growth_plan_recommendations_growth_plans_growth_plan_id",
                        column: x => x.growth_plan_id,
                        principalTable: "growth_plans",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "marketing_experiments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    exp_id = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    recommendation_rec_id = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    hypothesis = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    segment = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    proposed_campaign = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    primary_metric = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    measurement_period_days = table.Column<int>(type: "integer", nullable: false),
                    success_threshold = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    threshold_is_proposed_default = table.Column<bool>(type: "boolean", nullable: false),
                    if_success = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    if_failure = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "Proposed"),
                    observed_result = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    result_notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    growth_plan_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_marketing_experiments", x => x.id);
                    table.CheckConstraint("ck_marketing_experiments_period", "measurement_period_days between 1 and 90");
                    table.ForeignKey(
                        name: "fk_marketing_experiments_growth_plans_growth_plan_id",
                        column: x => x.growth_plan_id,
                        principalTable: "growth_plans",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "refresh_tokens",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    family_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    absolute_expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_reason = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    replaced_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_by_ip_hash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: true),
                    user_agent = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_refresh_tokens", x => x.id);
                    table.ForeignKey(
                        name: "fk_refresh_tokens_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_claims",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    claim_type = table.Column<string>(type: "text", nullable: true),
                    claim_value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_claims", x => x.id);
                    table.ForeignKey(
                        name: "fk_user_claims_asp_net_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_logins",
                columns: table => new
                {
                    login_provider = table.Column<string>(type: "text", nullable: false),
                    provider_key = table.Column<string>(type: "text", nullable: false),
                    provider_display_name = table.Column<string>(type: "text", nullable: true),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_logins", x => new { x.login_provider, x.provider_key });
                    table.ForeignKey(
                        name: "fk_user_logins_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_tokens",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    login_provider = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_tokens", x => new { x.user_id, x.login_provider, x.name });
                    table.ForeignKey(
                        name: "fk_user_tokens_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_business_profiles_owner_created",
                table: "business_profiles",
                columns: new[] { "owner_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_evaluation_runs_owner_created",
                table: "evaluation_runs",
                columns: new[] { "owner_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_growth_plan_evidence_growth_plan_id_evidence_key",
                table: "growth_plan_evidence",
                columns: new[] { "growth_plan_id", "evidence_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_growth_plan_evidence_growth_plan_id_qloo_entity_id",
                table: "growth_plan_evidence",
                columns: new[] { "growth_plan_id", "qloo_entity_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_growth_plan_recommendations_growth_plan_id_kind_ordinal",
                table: "growth_plan_recommendations",
                columns: new[] { "growth_plan_id", "kind", "ordinal" });

            migrationBuilder.CreateIndex(
                name: "ix_growth_plan_recommendations_growth_plan_id_rec_id",
                table: "growth_plan_recommendations",
                columns: new[] { "growth_plan_id", "rec_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_growth_plans_owner_created",
                table: "growth_plans",
                columns: new[] { "owner_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_growth_plans_profile_created",
                table: "growth_plans",
                columns: new[] { "business_profile_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_growth_plans_status_partial",
                table: "growth_plans",
                column: "status",
                filter: "status in ('Queued','Running')");

            migrationBuilder.CreateIndex(
                name: "ux_growth_plans_owner_idem",
                table: "growth_plans",
                columns: new[] { "owner_id", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_marketing_experiments_growth_plan_id_exp_id",
                table: "marketing_experiments",
                columns: new[] { "growth_plan_id", "exp_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_refresh_tokens_expires",
                table: "refresh_tokens",
                column: "expires_at");

            migrationBuilder.CreateIndex(
                name: "ix_refresh_tokens_user_family",
                table: "refresh_tokens",
                columns: new[] { "user_id", "family_id" });

            migrationBuilder.CreateIndex(
                name: "ux_refresh_tokens_hash",
                table: "refresh_tokens",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_user_claims_user_id",
                table: "user_claims",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_logins_user_id",
                table: "user_logins",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "EmailIndex",
                table: "users",
                column: "normalized_email");

            migrationBuilder.CreateIndex(
                name: "UserNameIndex",
                table: "users",
                column: "normalized_user_name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "business_profiles");

            migrationBuilder.DropTable(
                name: "evaluation_runs");

            migrationBuilder.DropTable(
                name: "growth_plan_evidence");

            migrationBuilder.DropTable(
                name: "growth_plan_recommendations");

            migrationBuilder.DropTable(
                name: "marketing_experiments");

            migrationBuilder.DropTable(
                name: "refresh_tokens");

            migrationBuilder.DropTable(
                name: "user_claims");

            migrationBuilder.DropTable(
                name: "user_logins");

            migrationBuilder.DropTable(
                name: "user_tokens");

            migrationBuilder.DropTable(
                name: "growth_plans");

            migrationBuilder.DropTable(
                name: "users");
        }
    }
}
