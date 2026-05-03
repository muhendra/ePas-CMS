using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using e_Pas_CMS.Models;

namespace e_Pas_CMS.Data;

public partial class EpasDbContext : DbContext
{
    public EpasDbContext(DbContextOptions<EpasDbContext> options)
        : base(options)
    {
    }

    public virtual DbSet<app_role> app_roles { get; set; }

    public virtual DbSet<app_user> app_users { get; set; }

    public virtual DbSet<app_user_role> app_user_roles { get; set; }

    public virtual DbSet<master_audit_flow> master_audit_flows { get; set; }

    public virtual DbSet<master_questioner> master_questioners { get; set; }

    public virtual DbSet<master_questioner_detail> master_questioner_details { get; set; }

    public virtual DbSet<spbu> spbus { get; set; }

    public virtual DbSet<spbu_image> spbu_images { get; set; }

    public virtual DbSet<trx_audit> trx_audits { get; set; }

    public virtual DbSet<trx_audit_checklist> trx_audit_checklists { get; set; }

    public virtual DbSet<trx_audit_medium> trx_audit_media { get; set; }

    public virtual DbSet<trx_audit_qq> trx_audit_qqs { get; set; }

    public virtual DbSet<TrxFeedback> TrxFeedbacks { get; set; }

    public virtual DbSet<TrxFeedbackPoint> TrxFeedbackPoints { get; set; }

    public virtual DbSet<TrxFeedbackPointElement> TrxFeedbackPointElements { get; set; }

    public virtual DbSet<TrxFeedbackPointMedium> TrxFeedbackPointMedia { get; set; }

    public virtual DbSet<TrxFeedbackPointApproval> TrxFeedbackPointApprovals { get; set; }

    public virtual DbSet<TrxSurvey> TrxSurveys { get; set; }

    public virtual DbSet<TrxSurveyElement> TrxSurveyElements { get; set; }

    public virtual DbSet<SysParameter> SysParameter { get; set; }

    public virtual DbSet<trx_audit_not_started_log> trx_audit_not_started_logs { get; set; }

    public DbSet<TrxInvoice> TrxInvoices { get; set; }
    public DbSet<TrxInvoiceDetail> TrxInvoiceDetails { get; set; }

    public DbSet<TrxInvoiceApproval> TrxInvoiceApprovals { get; set; }
    public DbSet<TrxInvoiceApprovalDetail> TrxInvoiceApprovalDetails { get; set; }

    public DbSet<trx_claim> TrxClaims { get; set; }
    public DbSet<trx_claim_detail> TrxClaimDetails { get; set; }
    public DbSet<trx_claim_media> TrxClaimMedias { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresExtension("uuid-ossp");
        // Prevent EF from accidentally mapping duplicate PascalCase scaffold entities.
        // Project ini pakai lowercase trx_audit/master_questioner di DbContext.
        modelBuilder.Ignore<TrxAudit>();
        modelBuilder.Ignore<MasterQuestioner>();
        modelBuilder.Ignore<TrxAuditChecklist>();
        modelBuilder.Ignore<TrxAuditMedium>();
        modelBuilder.Ignore<TrxAuditQq>();
        modelBuilder.Entity<app_role>(entity =>
        {
            entity.HasKey(e => e.id).HasName("app_role_pkey");

            entity.ToTable("app_role");

            entity.HasIndex(e => new { e.app, e.name }, "ux_app_role_name").IsUnique();

            entity.Property(e => e.id)
                .HasMaxLength(50)
                .HasDefaultValueSql("uuid_generate_v4()");
            entity.Property(e => e.app).HasMaxLength(100);
            entity.Property(e => e.created_by).HasMaxLength(50);
            entity.Property(e => e.created_date)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp(3) without time zone");
            entity.Property(e => e.name).HasMaxLength(100);
            entity.Property(e => e.status).HasMaxLength(100);
            entity.Property(e => e.updated_by).HasMaxLength(50);
            entity.Property(e => e.updated_date)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone");
        });

        modelBuilder.Entity<app_user>(entity =>
        {
            entity.HasKey(e => e.id).HasName("app_user_pkey");

            entity.ToTable("app_user");

            entity.HasIndex(e => e.username, "ux_app_user_username").IsUnique();

            modelBuilder.Entity<TrxFeedbackPointApproval>(entity =>
            {
                entity.HasKey(e => e.Id).HasName("trx_feedback_point_approval_pkey");

                entity.ToTable("trx_feedback_point_approval");

                entity.Property(e => e.Id)
                    .HasMaxLength(50)
                    .HasColumnName("id")
                    .HasDefaultValueSql("uuid_generate_v4()");

                entity.Property(e => e.TrxFeedbackPointId)
                    .HasMaxLength(50)
                    .HasColumnName("trx_feedback_point_id")
                    .IsRequired();

                entity.Property(e => e.Status)
                    .HasMaxLength(100)
                    .HasColumnName("status")
                    .IsRequired();

                entity.Property(e => e.Notes)
                    .HasColumnName("notes")
                    .HasColumnType("text");

                entity.Property(e => e.ApprovedBy)
                    .HasMaxLength(50)
                    .HasColumnName("approved_by")
                    .IsRequired();

                entity.Property(e => e.ApprovedDate)
                    .HasColumnName("approved_date")
                    .HasColumnType("timestamp without time zone")
                    .HasDefaultValueSql("CURRENT_TIMESTAMP");
            });

            entity.Property(e => e.id)
                .HasMaxLength(50)
                .HasDefaultValueSql("uuid_generate_v4()");
            entity.Property(e => e.created_by).HasMaxLength(50);
            entity.Property(e => e.created_date)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp(3) without time zone");
            entity.Property(e => e.email).HasMaxLength(100);
            entity.Property(e => e.last_change_passwd_dt).HasColumnType("timestamp(3) without time zone");
            entity.Property(e => e.last_login_dt).HasColumnType("timestamp(3) without time zone");
            entity.Property(e => e.name).HasMaxLength(100);
            entity.Property(e => e.notification_token).HasMaxLength(255);
            entity.Property(e => e.password_hash).HasMaxLength(60);
            entity.Property(e => e.phone_number).HasMaxLength(50);
            entity.Property(e => e.status).HasMaxLength(100);
            entity.Property(e => e.suffix_refresh_token).HasMaxLength(25);
            entity.Property(e => e.updated_by).HasMaxLength(50);
            entity.Property(e => e.updated_date)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone");
            entity.Property(e => e.username).HasMaxLength(50);
        });

        modelBuilder.Entity<app_user_role>(entity =>
        {
            entity.HasKey(e => e.id).HasName("app_user_role_pkey");

            entity.ToTable("app_user_role");

            entity.Property(e => e.id)
                .HasMaxLength(50)
                .HasDefaultValueSql("uuid_generate_v4()");
            entity.Property(e => e.app_role_id).HasMaxLength(50);
            entity.Property(e => e.app_user_id).HasMaxLength(50);
            entity.Property(e => e.spbu_id).HasMaxLength(50);

            entity.HasOne(d => d.app_role).WithMany(p => p.app_user_roles)
                .HasForeignKey(d => d.app_role_id)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("app_user_role_app_role_id_fkey");

            entity.HasOne(d => d.app_user).WithMany(p => p.app_user_roles)
                .HasForeignKey(d => d.app_user_id)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("app_user_role_app_user_id_fkey");

            entity.HasOne(d => d.spbu).WithMany(p => p.app_user_roles)
                .HasForeignKey(d => d.spbu_id)
                .HasConstraintName("app_user_role_spbu_id_fkey");
        });

        modelBuilder.Entity<trx_audit_not_started_log>(entity =>
        {
            entity.ToTable("trx_audit_not_started_log");

            entity.HasKey(e => e.id);

            entity.Property(e => e.id)
                .HasMaxLength(50);

            entity.Property(e => e.trx_audit_id)
                .HasMaxLength(50)
                .IsRequired();

            entity.Property(e => e.spbu_id)
                .HasMaxLength(50);

            entity.Property(e => e.old_status)
                .HasMaxLength(100);

            entity.Property(e => e.new_status)
                .HasMaxLength(100)
                .IsRequired();

            entity.Property(e => e.old_form_status_auditor1)
                .HasMaxLength(100);

            entity.Property(e => e.new_form_status_auditor1)
                .HasMaxLength(100);

            entity.Property(e => e.changed_by)
                .HasMaxLength(50)
                .IsRequired();

            entity.Property(e => e.note);

            entity.Property(e => e.changed_date)
                .HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.HasOne(d => d.trx_audit)
                .WithMany()
                .HasForeignKey(d => d.trx_audit_id);

            entity.HasOne(d => d.spbu)
                .WithMany()
                .HasForeignKey(d => d.spbu_id);
        });


        modelBuilder.Entity<master_audit_flow>(entity =>
        {
            entity.HasKey(e => e.id).HasName("master_audit_flow_pkey");

            entity.ToTable("master_audit_flow");

            entity.Property(e => e.id)
                .HasMaxLength(50)
                .HasDefaultValueSql("uuid_generate_v4()");
            entity.Property(e => e.audit_level).HasMaxLength(100);
            entity.Property(e => e.created_by).HasMaxLength(50);
            entity.Property(e => e.created_date)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp(3) without time zone");
            entity.Property(e => e.failed_audit_level).HasMaxLength(100);
            entity.Property(e => e.passed_audit_level).HasMaxLength(100);
            entity.Property(e => e.status).HasMaxLength(100);
            entity.Property(e => e.updated_by).HasMaxLength(50);
            entity.Property(e => e.updated_date)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone");
        });

        modelBuilder.Entity<master_questioner>(entity =>
        {
            entity.HasKey(e => e.id).HasName("master_questioner_pkey");

            entity.ToTable("master_questioner");

            entity.Property(e => e.id)
                .HasMaxLength(50)
                .HasDefaultValueSql("uuid_generate_v4()");
            entity.Property(e => e.category).HasMaxLength(50);
            entity.Property(e => e.created_by).HasMaxLength(50);
            entity.Property(e => e.created_date)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp(3) without time zone");
            entity.Property(e => e.status).HasMaxLength(100);
            entity.Property(e => e.type).HasMaxLength(50);
            entity.Property(e => e.updated_by).HasMaxLength(50);
            entity.Property(e => e.updated_date)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone");
            entity.Property(e => e.version).HasDefaultValue(0);
        });

        modelBuilder.Entity<master_questioner_detail>(entity =>
        {
            entity.HasKey(e => e.id).HasName("master_questioner_detail_pkey");

            entity.ToTable("master_questioner_detail");

            entity.Property(e => e.id)
                .HasMaxLength(50)
                .HasDefaultValueSql("uuid_generate_v4()");
            entity.Property(e => e.created_by).HasMaxLength(50);
            entity.Property(e => e.created_date)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp(3) without time zone");
            entity.Property(e => e.master_questioner_id).HasMaxLength(50);
            entity.Property(e => e.parent_id).HasMaxLength(50);
            entity.Property(e => e.status).HasMaxLength(100);
            entity.Property(e => e.type).HasMaxLength(50);
            entity.Property(e => e.form_type).HasMaxLength(100);
            entity.Property(e => e.updated_by).HasMaxLength(50);
            entity.Property(e => e.updated_date)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone");
            entity.Property(e => e.weight).HasPrecision(5, 2);

            entity.HasOne(d => d.master_questioner).WithMany(p => p.master_questioner_details)
                .HasForeignKey(d => d.master_questioner_id)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("master_questioner_detail_master_questioner_id_fkey");

            entity.HasOne(d => d.parent).WithMany(p => p.Inverseparent)
                .HasForeignKey(d => d.parent_id)
                .HasConstraintName("master_questioner_detail_parent_id_fkey");
        });

        modelBuilder.Entity<Notification>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("notification_pkey");

            entity.ToTable("notification");

            entity.Property(e => e.Id)
                .HasMaxLength(50)
                .HasDefaultValueSql("uuid_generate_v4()")
                .HasColumnName("id");
            entity.Property(e => e.AppUserId)
                .HasMaxLength(50)
                .HasColumnName("app_user_id");
            entity.Property(e => e.CreatedBy)
                .HasMaxLength(50)
                .HasColumnName("created_by");
            entity.Property(e => e.CreatedDate)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("created_date");
            entity.Property(e => e.Message).HasColumnName("message");
            entity.Property(e => e.Status)
                .HasMaxLength(100)
                .HasColumnName("status");
            entity.Property(e => e.Title).HasColumnName("title");
            entity.Property(e => e.UpdatedBy)
                .HasMaxLength(50)
                .HasColumnName("updated_by");
            entity.Property(e => e.UpdatedDate)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("updated_date");

            entity.HasOne(d => d.AppUser).WithMany(p => p.Notifications)
                .HasForeignKey(d => d.AppUserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("notification_app_user_id_fkey");
        });

        modelBuilder.Entity<spbu>(entity =>
        {
            entity.HasKey(e => e.id).HasName("spbu_pkey");

            entity.ToTable("spbu");

            entity.Property(e => e.id)
                .HasMaxLength(50)
                .HasDefaultValueSql("uuid_generate_v4()");
            entity.Property(e => e.audit_current).HasMaxLength(100);
            entity.Property(e => e.audit_current_score).HasPrecision(5, 2);
            entity.Property(e => e.audit_current_time).HasColumnType("timestamp(3) without time zone");
            entity.Property(e => e.audit_next).HasMaxLength(100);
            entity.Property(e => e.city_name).HasMaxLength(255);
            entity.Property(e => e.created_by).HasMaxLength(50);
            entity.Property(e => e.created_date)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp(3) without time zone");
            entity.Property(e => e.level).HasMaxLength(255);
            entity.Property(e => e.manager_name).HasMaxLength(255);
            entity.Property(e => e.mor).HasMaxLength(100);
            entity.Property(e => e.owner_name).HasMaxLength(255);
            entity.Property(e => e.owner_type).HasMaxLength(100);
            entity.Property(e => e.phone_number_1).HasMaxLength(255);
            entity.Property(e => e.phone_number_2).HasMaxLength(255);
            entity.Property(e => e.province_name).HasMaxLength(255);
            entity.Property(e => e.region).HasMaxLength(10);
            entity.Property(e => e.sales_area).HasMaxLength(255);
            entity.Property(e => e.sam).HasMaxLength(255);
            entity.Property(e => e.sbm).HasMaxLength(255);
            entity.Property(e => e.spbu_no).HasMaxLength(100);
            entity.Property(e => e.status).HasMaxLength(100);
            entity.Property(e => e.status_excellent).HasMaxLength(100);
            entity.Property(e => e.status_good).HasMaxLength(100);
            entity.Property(e => e.type).HasMaxLength(255);
            entity.Property(e => e.updated_by).HasMaxLength(50);
            entity.Property(e => e.updated_date)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone");
        });

        modelBuilder.Entity<spbu_image>(entity =>
        {
            entity.HasKey(e => e.id).HasName("spbu_image_pkey");

            entity.ToTable("spbu_image");

            entity.Property(e => e.id)
                .HasMaxLength(50)
                .HasDefaultValueSql("uuid_generate_v4()");
            entity.Property(e => e.filepath).HasMaxLength(255);
            entity.Property(e => e.spbu_id).HasMaxLength(50);

            entity.HasOne(d => d.spbu).WithMany(p => p.spbu_images)
                .HasForeignKey(d => d.spbu_id)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("spbu_image_spbu_fkey");
        });

        modelBuilder.Entity<trx_audit>(entity =>
        {
            entity.HasKey(e => e.id).HasName("trx_audit_pkey");

            entity.ToTable("trx_audit");

            entity.Property(e => e.id)
                .HasMaxLength(50)
                .HasDefaultValueSql("uuid_generate_v4()");
            entity.Property(e => e.app_user_id).HasMaxLength(50);
            entity.Property(e => e.audit_execution_time).HasColumnType("timestamp(3) without time zone");
            entity.Property(e => e.audit_level).HasMaxLength(100);
            entity.Property(e => e.audit_type).HasMaxLength(100);
            entity.Property(e => e.created_by).HasMaxLength(50);
            entity.Property(e => e.created_date)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp(3) without time zone");
            entity.Property(e => e.master_questioner_checklist_id).HasMaxLength(50);
            entity.Property(e => e.master_questioner_intro_id).HasMaxLength(50);
            entity.Property(e => e.report_no).HasMaxLength(50);
            entity.Property(e => e.report_prefix).HasMaxLength(50);
            entity.Property(e => e.spbu_id).HasMaxLength(50);
            entity.Property(e => e.status).HasMaxLength(100);
            entity.Property(e => e.updated_by).HasMaxLength(50);
            entity.Property(e => e.updated_date)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone");

            entity.HasOne(d => d.app_user).WithMany(p => p.trx_audits)
                .HasForeignKey(d => d.app_user_id)
                .HasConstraintName("trx_audit_app_user_id_fkey");

            entity.HasOne(d => d.master_questioner_checklist).WithMany(p => p.trx_auditmaster_questioner_checklists)
                .HasForeignKey(d => d.master_questioner_checklist_id)
                .HasConstraintName("trx_audit_master_questioner_checklist_id_id_fkey");

            entity.HasOne(d => d.master_questioner_intro).WithMany(p => p.trx_auditmaster_questioner_intros)
                .HasForeignKey(d => d.master_questioner_intro_id)
                .HasConstraintName("trx_audit_master_questioner_intro_id_fkey");

            entity.HasOne(d => d.spbu).WithMany(p => p.trx_audits)
                .HasForeignKey(d => d.spbu_id)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("trx_audit_spbu_id_fkey");
        });

        modelBuilder.Entity<trx_audit_checklist>(entity =>
        {
            entity.HasKey(e => e.id).HasName("trx_audit_checklist_pkey");

            entity.ToTable("trx_audit_checklist");

            entity.Property(e => e.id)
                .HasMaxLength(50)
                .HasDefaultValueSql("uuid_generate_v4()");
            entity.Property(e => e.created_by).HasMaxLength(50);
            entity.Property(e => e.created_date)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp(3) without time zone");
            entity.Property(e => e.master_questioner_detail_id).HasMaxLength(50);
            entity.Property(e => e.score_af).HasPrecision(5, 2);
            entity.Property(e => e.score_input).HasMaxLength(50);
            entity.Property(e => e.score_x).HasPrecision(5, 2);
            entity.Property(e => e.status).HasMaxLength(100);
            entity.Property(e => e.trx_audit_id).HasMaxLength(50);
            entity.Property(e => e.updated_by).HasMaxLength(50);
            entity.Property(e => e.updated_date)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone");

            entity.HasOne(d => d.master_questioner_detail).WithMany(p => p.trx_audit_checklists)
                .HasForeignKey(d => d.master_questioner_detail_id)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("trx_audit_checklist_master_questioner_detail_id_fkey");

            entity.HasOne(d => d.trx_audit).WithMany(p => p.trx_audit_checklists)
                .HasForeignKey(d => d.trx_audit_id)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("trx_audit_checklist_trx_audit_id_fkey");
        });

        modelBuilder.Entity<trx_audit_medium>(entity =>
        {
            entity.HasKey(e => e.id).HasName("trx_audit_media_pkey");

            entity.Property(e => e.id)
                .HasMaxLength(50)
                .HasDefaultValueSql("uuid_generate_v4()");
            entity.Property(e => e.created_by).HasMaxLength(50);
            entity.Property(e => e.created_date)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp(3) without time zone");
            entity.Property(e => e.master_questioner_detail_id).HasMaxLength(50);
            entity.Property(e => e.media_type).HasMaxLength(100);
            entity.Property(e => e.status).HasMaxLength(100);
            entity.Property(e => e.trx_audit_id).HasMaxLength(50);
            entity.Property(e => e.type).HasMaxLength(50);
            entity.Property(e => e.updated_by).HasMaxLength(50);
            entity.Property(e => e.updated_date)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone");

            entity.HasOne(d => d.master_questioner_detail).WithMany(p => p.trx_audit_media)
                .HasForeignKey(d => d.master_questioner_detail_id)
                .HasConstraintName("trx_audit_media_master_questioner_detail_id_fkey");

            entity.HasOne(d => d.trx_audit).WithMany(p => p.trx_audit_media)
                .HasForeignKey(d => d.trx_audit_id)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("trx_audit_media_trx_audit_id_fkey");
        });

        modelBuilder.Entity<trx_audit_qq>(entity =>
        {
            entity.HasKey(e => e.id).HasName("trx_audit_qq_pkey");

            entity.ToTable("trx_audit_qq");

            entity.Property(e => e.id)
                .HasMaxLength(50)
                .HasDefaultValueSql("uuid_generate_v4()");
            entity.Property(e => e.created_by).HasMaxLength(50);
            entity.Property(e => e.created_date)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp(3) without time zone");
            entity.Property(e => e.du_make).HasMaxLength(255);
            entity.Property(e => e.du_serial_no).HasMaxLength(255);
            entity.Property(e => e.mode).HasMaxLength(50);
            entity.Property(e => e.nozzle_number).HasMaxLength(50);
            entity.Property(e => e.product).HasMaxLength(255);
            entity.Property(e => e.status).HasMaxLength(100);
            entity.Property(e => e.trx_audit_id).HasMaxLength(50);
            entity.Property(e => e.updated_by).HasMaxLength(50);
            entity.Property(e => e.updated_date)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone");

            entity.HasOne(d => d.trx_audit).WithMany(p => p.trx_audit_qqs)
                .HasForeignKey(d => d.trx_audit_id)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("trx_audit_qq_trx_audit_id_fkey");
        });

        modelBuilder.Entity<TrxFeedback>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("trx_feedback_pkey");

            entity.ToTable("trx_feedback");

            entity.Property(e => e.Id)
                .HasMaxLength(50)
                .HasDefaultValueSql("uuid_generate_v4()")
                .HasColumnName("id");
            entity.Property(e => e.AppUserId)
                .HasMaxLength(50)
                .HasColumnName("app_user_id");
            entity.Property(e => e.ApprovalBy)
                .HasMaxLength(50)
                .HasColumnName("approval_by");
            entity.Property(e => e.ApprovalDate)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("approval_date");
            entity.Property(e => e.CreatedBy)
                .HasMaxLength(50)
                .HasColumnName("created_by");
            entity.Property(e => e.CreatedDate)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("created_date");
            entity.Property(e => e.FeedbackType)
                .HasMaxLength(50)
                .HasColumnName("feedback_type");
            entity.Property(e => e.Status)
                .HasMaxLength(100)
                .HasColumnName("status");
            entity.Property(e => e.TicketNo)
                .HasMaxLength(50)
                .HasColumnName("ticket_no");
            entity.Property(e => e.TrxAuditId)
                .HasMaxLength(50)
                .HasColumnName("trx_audit_id");
            entity.Property(e => e.UpdatedBy)
                .HasMaxLength(50)
                .HasColumnName("updated_by");
            entity.Property(e => e.UpdatedDate)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("updated_date");
            entity.Property(e => e.Klarifikasi)
                .HasMaxLength(2000)
                .HasColumnName("klarifikasi");
            entity.Property(e => e.Next_audit_before)
                .HasMaxLength(500)
                .HasColumnName("next_audit_before");
            entity.HasOne(d => d.AppUser).WithMany(p => p.TrxFeedbacks)
                .HasForeignKey(d => d.AppUserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("trx_feedback_app_user_id_fkey");

            entity.HasOne(d => d.TrxAudit).WithMany(p => p.TrxFeedbacks)
                .HasForeignKey(d => d.TrxAuditId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("trx_feedback_trx_audit_id_fkey");
        });

        modelBuilder.Entity<TrxFeedbackPoint>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("trx_feedback_point_pkey");

            entity.ToTable("trx_feedback_point");

            entity.Property(e => e.Id)
                .HasMaxLength(50)
                .HasDefaultValueSql("uuid_generate_v4()")
                .HasColumnName("id");
            entity.Property(e => e.CreatedBy)
                .HasMaxLength(50)
                .HasColumnName("created_by");
            entity.Property(e => e.CreatedDate)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("created_date");
            entity.Property(e => e.Description).HasColumnName("description");
            entity.Property(e => e.DetailElementMasterQuestionerDetailId)
                .HasMaxLength(50)
                .HasColumnName("detail_element_master_questioner_detail_id");
            entity.Property(e => e.ElementMasterQuestionerDetailId)
                .HasMaxLength(50)
                .HasColumnName("element_master_questioner_detail_id");
            entity.Property(e => e.MediaTotal).HasColumnName("media_total");
            entity.Property(e => e.MediaUpload).HasColumnName("media_upload");
            entity.Property(e => e.Status)
                .HasMaxLength(100)
                .HasColumnName("status");
            entity.Property(e => e.SubElementMasterQuestionerDetailId)
                .HasMaxLength(50)
                .HasColumnName("sub_element_master_questioner_detail_id");
            entity.Property(e => e.TrxFeedbackId)
                .HasMaxLength(50)
                .HasColumnName("trx_feedback_id");
            entity.Property(e => e.UpdatedBy)
                .HasMaxLength(50)
                .HasColumnName("updated_by");
            entity.Property(e => e.UpdatedDate)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("updated_date");

            entity.HasOne(d => d.ElementMasterQuestionerDetail).WithMany(p => p.TrxFeedbackPoints)
                .HasForeignKey(d => d.ElementMasterQuestionerDetailId)
                .HasConstraintName("trx_feedback_point_element_master_questioner_detail_id_fkey");

            entity.HasOne(d => d.TrxFeedback).WithMany(p => p.TrxFeedbackPoints)
                .HasForeignKey(d => d.TrxFeedbackId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("trx_feedback_point_trx_feedback_id_fkey");
        });

        modelBuilder.Entity<TrxFeedbackPointElement>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("trx_feedback_point_element_pkey");

            entity.ToTable("trx_feedback_point_element");

            entity.Property(e => e.Id)
                .HasMaxLength(50)
                .HasDefaultValueSql("uuid_generate_v4()")
                .HasColumnName("id");
            entity.Property(e => e.CreatedBy)
                .HasMaxLength(50)
                .HasColumnName("created_by");
            entity.Property(e => e.CreatedDate)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("created_date");
            entity.Property(e => e.MasterQuestionerDetailId)
                .HasMaxLength(50)
                .HasColumnName("master_questioner_detail_id");
            entity.Property(e => e.Status)
                .HasMaxLength(100)
                .HasColumnName("status");
            entity.Property(e => e.TrxFeedbackPointId)
                .HasMaxLength(50)
                .HasColumnName("trx_feedback_point_id");
            entity.Property(e => e.UpdatedBy)
                .HasMaxLength(50)
                .HasColumnName("updated_by");
            entity.Property(e => e.UpdatedDate)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("updated_date");

            entity.HasOne(d => d.MasterQuestionerDetail).WithMany(p => p.TrxFeedbackPointElements)
                .HasForeignKey(d => d.MasterQuestionerDetailId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("trx_feedback_point_element_master_questioner_detail_id_fkey");

            entity.HasOne(d => d.TrxFeedbackPoint).WithMany(p => p.TrxFeedbackPointElements)
                .HasForeignKey(d => d.TrxFeedbackPointId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("trx_feedback_point_element_trx_feedback_point_id_fkey");
        });

        modelBuilder.Entity<TrxFeedbackPointMedium>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("trx_feedback_point_media_pkey");

            entity.ToTable("trx_feedback_point_media");

            entity.Property(e => e.Id)
                .HasMaxLength(50)
                .HasDefaultValueSql("uuid_generate_v4()")
                .HasColumnName("id");
            entity.Property(e => e.CreatedBy)
                .HasMaxLength(50)
                .HasColumnName("created_by");
            entity.Property(e => e.CreatedDate)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("created_date");
            entity.Property(e => e.MediaPath).HasColumnName("media_path");
            entity.Property(e => e.MediaType)
                .HasMaxLength(100)
                .HasColumnName("media_type");
            entity.Property(e => e.Status)
                .HasMaxLength(100)
                .HasColumnName("status");
            entity.Property(e => e.TrxFeedbackPointId)
                .HasMaxLength(50)
                .HasColumnName("trx_feedback_point_id");
            entity.Property(e => e.UpdatedBy)
                .HasMaxLength(50)
                .HasColumnName("updated_by");
            entity.Property(e => e.UpdatedDate)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("updated_date");

            entity.HasOne(d => d.TrxFeedbackPoint).WithMany(p => p.TrxFeedbackPointMedia)
                .HasForeignKey(d => d.TrxFeedbackPointId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("trx_feedback_point_media_trx_feedback_point_id_fkey");
        });

        modelBuilder.Entity<TrxSurvey>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("trx_survey_pkey");

            entity.ToTable("trx_survey");

            entity.HasIndex(e => e.MasterQuestionerId, "idx_ts_master_questioner_id");

            entity.Property(e => e.Id)
                .HasMaxLength(50)
                .HasDefaultValueSql("uuid_generate_v4()")
                .HasColumnName("id");
            entity.Property(e => e.AppUserId)
                .HasMaxLength(50)
                .HasColumnName("app_user_id");
            entity.Property(e => e.CreatedBy)
                .HasMaxLength(50)
                .HasColumnName("created_by");
            entity.Property(e => e.CreatedDate)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("created_date");
            entity.Property(e => e.MasterQuestionerId)
                .HasMaxLength(50)
                .HasColumnName("master_questioner_id");
            entity.Property(e => e.Status)
                .HasMaxLength(100)
                .HasColumnName("status");
            entity.Property(e => e.UpdatedBy)
                .HasMaxLength(50)
                .HasColumnName("updated_by");
            entity.Property(e => e.UpdatedDate)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("updated_date");

            entity.HasOne(d => d.AppUser).WithMany(p => p.TrxSurveys)
                .HasForeignKey(d => d.AppUserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("trx_survey_app_user_id_fkey");

            entity.HasOne(d => d.MasterQuestioner).WithMany(p => p.TrxSurveys)
                .HasForeignKey(d => d.MasterQuestionerId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("trx_survey_master_questioner_id_fkey");
        });

        modelBuilder.Entity<TrxSurveyElement>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("trx_survey_element_pkey");

            entity.ToTable("trx_survey_element");

            entity.HasIndex(e => e.MasterQuestionerDetailId, "idx_tse_master_questioner_detail_id");

            entity.HasIndex(e => e.TrxSurveyId, "idx_tse_trx_survey_id");

            entity.Property(e => e.Id)
                .HasMaxLength(50)
                .HasDefaultValueSql("uuid_generate_v4()")
                .HasColumnName("id");
            entity.Property(e => e.CreatedBy)
                .HasMaxLength(50)
                .HasColumnName("created_by");
            entity.Property(e => e.CreatedDate)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("created_date");
            entity.Property(e => e.MasterQuestionerDetailId)
                .HasMaxLength(50)
                .HasColumnName("master_questioner_detail_id");
            entity.Property(e => e.ScoreInput)
                .HasMaxLength(255)
                .HasColumnName("score_input");
            entity.Property(e => e.Status)
                .HasMaxLength(100)
                .HasColumnName("status");
            entity.Property(e => e.TrxSurveyId)
                .HasMaxLength(50)
                .HasColumnName("trx_survey_id");
            entity.Property(e => e.UpdatedBy)
                .HasMaxLength(50)
                .HasColumnName("updated_by");
            entity.Property(e => e.UpdatedDate)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("updated_date");

            entity.HasOne(d => d.MasterQuestionerDetail).WithMany(p => p.TrxSurveyElements)
                .HasForeignKey(d => d.MasterQuestionerDetailId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("trx_survey_element_master_questioner_detail_id_fkey");

            entity.HasOne(d => d.TrxSurvey).WithMany(p => p.TrxSurveyElements)
                .HasForeignKey(d => d.TrxSurveyId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("trx_survey_element_trx_survey_id_fkey");
        });
        modelBuilder.Entity<SysParameter>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("sys_parameter_pkey");

            entity.ToTable("sys_parameter");

            // unique index sesuai constraint
            entity.HasIndex(e => e.Code, "sys_parameter_code_uk").IsUnique();

            entity.Property(e => e.Id)
                .HasMaxLength(50)
                .HasColumnName("id")
                .HasDefaultValueSql("uuid_generate_v4()");

            entity.Property(e => e.Code)
                .HasMaxLength(255)
                .HasColumnName("code")
                .IsRequired();

            entity.Property(e => e.Value)
                .HasMaxLength(255)
                .HasColumnName("value")
                .IsRequired();

            entity.Property(e => e.Status)
                .HasMaxLength(100)
                .HasColumnName("status")
                .IsRequired();

            entity.Property(e => e.CreatedBy)
                .HasMaxLength(50)
                .HasColumnName("created_by")
                .IsRequired();

            entity.Property(e => e.CreatedDate)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("created_date");

            entity.Property(e => e.UpdatedBy)
                .HasMaxLength(50)
                .HasColumnName("updated_by")
                .IsRequired();

            entity.Property(e => e.UpdatedDate)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("updated_date");
        });
        modelBuilder.Entity<TrxInvoice>(entity =>
        {
            entity.ToTable("trx_invoice");

            entity.HasKey(e => e.Id).HasName("trx_invoice_pkey");

            entity.Property(e => e.Id)
                .HasColumnName("id")
                .HasMaxLength(50)
                .HasDefaultValueSql("uuid_generate_v4()");

            entity.Property(e => e.AppUserId)
                .HasColumnName("app_user_id")
                .HasMaxLength(50);

            entity.Property(e => e.InvoicePrefix)
                .HasColumnName("invoice_prefix")
                .HasMaxLength(50);

            entity.Property(e => e.InvoiceNo)
                .HasColumnName("invoice_no")
                .HasMaxLength(100)
                .IsRequired();

            entity.Property(e => e.InvoicePeriodStart)
                .HasColumnName("invoice_period_start")
                .IsRequired();

            entity.Property(e => e.InvoicePeriodEnd)
                .HasColumnName("invoice_period_end")
                .IsRequired();

            entity.Property(e => e.IssuedDate)
                .HasColumnName("issued_date");

            entity.Property(e => e.DueDate)
                .HasColumnName("due_date");

            entity.Property(e => e.CompletedDate)
                .HasColumnName("completed_date");

            entity.Property(e => e.Status)
                .HasColumnName("status")
                .HasMaxLength(50)
                .HasDefaultValue("NOT_CLAIMED")
                .IsRequired();

            entity.Property(e => e.CreatedBy)
                .HasColumnName("created_by")
                .HasMaxLength(50)
                .IsRequired();

            entity.Property(e => e.CreatedDate)
                .HasColumnName("created_date")
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .ValueGeneratedOnAdd();

            entity.Property(e => e.UpdatedBy)
                .HasColumnName("updated_by")
                .HasMaxLength(50)
                .IsRequired();

            entity.Property(e => e.UpdatedDate)
                .HasColumnName("updated_date")
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .ValueGeneratedOnAddOrUpdate();
        });

        modelBuilder.Entity<TrxInvoiceDetail>(entity =>
        {
            entity.ToTable("trx_invoice_detail");

            entity.HasKey(e => e.Id).HasName("trx_invoice_detail_pkey");

            entity.Property(e => e.Id)
                .HasColumnName("id")
                .HasMaxLength(50)
                .HasDefaultValueSql("uuid_generate_v4()");

            entity.Property(e => e.TrxInvoiceId)
                .HasColumnName("trx_invoice_id")
                .HasMaxLength(50)
                .IsRequired();

            entity.Property(e => e.TrxAuditId)
                .HasColumnName("trx_audit_id")
                .HasMaxLength(50)
                .IsRequired();

            entity.Property(e => e.AuditFee)
                .HasColumnName("audit_fee")
                .HasColumnType("numeric(18,2)")
                .HasDefaultValue(0m)
                .IsRequired();

            entity.Property(e => e.LumpsumFee)
                .HasColumnName("lumpsum_fee")
                .HasColumnType("numeric(18,2)")
                .HasDefaultValue(0m);

            entity.Property(e => e.Status)
                .HasColumnName("status")
                .HasMaxLength(50)
                .HasDefaultValue("NOT_CLAIMED")
                .IsRequired();

            entity.Property(e => e.CreatedBy)
                .HasColumnName("created_by")
                .HasMaxLength(50)
                .IsRequired();

            entity.Property(e => e.CreatedDate)
                .HasColumnName("created_date")
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .ValueGeneratedOnAdd();

            entity.Property(e => e.UpdatedBy)
                .HasColumnName("updated_by")
                .HasMaxLength(50)
                .IsRequired();

            entity.Property(e => e.UpdatedDate)
                .HasColumnName("updated_date")
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .ValueGeneratedOnAddOrUpdate();

            entity.HasOne(d => d.Invoice)
                .WithMany(p => p.Details)
                .HasForeignKey(d => d.TrxInvoiceId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("trx_invoice_detail_trx_invoice_id_fkey");
        });

        modelBuilder.Entity<TrxInvoiceApproval>(entity =>
        {
            entity.ToTable("trx_invoice_approval");

            entity.HasKey(e => e.Id).HasName("trx_invoice_approval_pkey");

            entity.HasIndex(e => e.TrxInvoiceId, "idx_trx_invoice_approval_invoice");

            entity.HasIndex(e => e.TrxClaimId, "idx_trx_invoice_approval_claim");

            entity.Property(e => e.Id)
                .HasColumnName("id")
                .HasMaxLength(50)
                .HasDefaultValueSql("uuid_generate_v4()");

            entity.Property(e => e.TrxInvoiceId)
                .HasColumnName("trx_invoice_id")
                .HasMaxLength(50)
                .IsRequired();

            entity.Property(e => e.TrxClaimId)
                .HasColumnName("trx_claim_id")
                .HasMaxLength(50)
                .IsRequired();

            entity.Property(e => e.ApprovalAction)
                .HasColumnName("approval_action")
                .HasMaxLength(50)
                .IsRequired();

            entity.Property(e => e.ClaimExpenseAmount)
                .HasColumnName("claim_expense_amount")
                .HasColumnType("numeric(18,2)")
                .HasDefaultValue(0m)
                .IsRequired();

            entity.Property(e => e.TotalAuditFee)
                .HasColumnName("total_audit_fee")
                .HasColumnType("numeric(18,2)")
                .HasDefaultValue(0m)
                .IsRequired();

            entity.Property(e => e.TotalLumpsumFee)
                .HasColumnName("total_lumpsum_fee")
                .HasColumnType("numeric(18,2)")
                .HasDefaultValue(0m)
                .IsRequired();

            entity.Property(e => e.TotalExpense)
                .HasColumnName("total_expense")
                .HasColumnType("numeric(18,2)")
                .HasDefaultValue(0m)
                .IsRequired();

            entity.Property(e => e.RejectionReason)
                .HasColumnName("rejection_reason")
                .HasColumnType("text");

            entity.Property(e => e.ApprovedBy)
                .HasColumnName("approved_by")
                .HasMaxLength(50)
                .IsRequired();

            entity.Property(e => e.ApprovedDate)
                .HasColumnName("approved_date")
                .HasColumnType("timestamp(3) without time zone")
                .HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.Property(e => e.CreatedBy)
                .HasColumnName("created_by")
                .HasMaxLength(50)
                .IsRequired();

            entity.Property(e => e.CreatedDate)
                .HasColumnName("created_date")
                .HasColumnType("timestamp(3) without time zone")
                .HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.HasOne(d => d.TrxInvoice)
                .WithMany(p => p.TrxInvoiceApprovals)
                .HasForeignKey(d => d.TrxInvoiceId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("trx_invoice_approval_invoice_fkey");

            entity.HasOne(d => d.TrxClaim)
                .WithMany(p => p.TrxInvoiceApprovals)
                .HasForeignKey(d => d.TrxClaimId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("trx_invoice_approval_claim_fkey");
        });

        modelBuilder.Entity<TrxInvoiceApprovalDetail>(entity =>
        {
            entity.ToTable("trx_invoice_approval_detail");

            entity.HasKey(e => e.Id).HasName("trx_invoice_approval_detail_pkey");

            entity.HasIndex(e => e.TrxInvoiceApprovalId, "idx_trx_invoice_approval_detail_header");

            entity.HasIndex(e => e.TrxInvoiceDetailId, "idx_trx_invoice_approval_detail_invoice_detail");

            entity.Property(e => e.Id)
                .HasColumnName("id")
                .HasMaxLength(50)
                .HasDefaultValueSql("uuid_generate_v4()");

            entity.Property(e => e.TrxInvoiceApprovalId)
                .HasColumnName("trx_invoice_approval_id")
                .HasMaxLength(50)
                .IsRequired();

            entity.Property(e => e.TrxInvoiceDetailId)
                .HasColumnName("trx_invoice_detail_id")
                .HasMaxLength(50)
                .IsRequired();

            entity.Property(e => e.TrxAuditId)
                .HasColumnName("trx_audit_id")
                .HasMaxLength(50)
                .IsRequired();

            entity.Property(e => e.AuditFee)
                .HasColumnName("audit_fee")
                .HasColumnType("numeric(18,2)")
                .HasDefaultValue(0m)
                .IsRequired();

            entity.Property(e => e.LumpsumFee)
                .HasColumnName("lumpsum_fee")
                .HasColumnType("numeric(18,2)")
                .HasDefaultValue(0m)
                .IsRequired();

            entity.Property(e => e.LineTotal)
                .HasColumnName("line_total")
                .HasColumnType("numeric(18,2)")
                .HasDefaultValue(0m)
                .IsRequired();

            entity.Property(e => e.CreatedBy)
                .HasColumnName("created_by")
                .HasMaxLength(50)
                .IsRequired();

            entity.Property(e => e.CreatedDate)
                .HasColumnName("created_date")
                .HasColumnType("timestamp(3) without time zone")
                .HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.HasOne(d => d.TrxInvoiceApproval)
                .WithMany(p => p.TrxInvoiceApprovalDetails)
                .HasForeignKey(d => d.TrxInvoiceApprovalId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("trx_invoice_approval_detail_header_fkey");

            entity.HasOne(d => d.TrxInvoiceDetail)
                .WithMany(p => p.TrxInvoiceApprovalDetails)
                .HasForeignKey(d => d.TrxInvoiceDetailId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("trx_invoice_approval_detail_invoice_detail_fkey");
        });
        modelBuilder.Entity<trx_claim>(entity =>
        {
            entity.ToTable("trx_claim");

            entity.HasKey(e => e.id).HasName("trx_claim_pkey");

            entity.Property(e => e.id)
                .HasColumnName("id")
                .HasMaxLength(50)
                .HasDefaultValueSql("uuid_generate_v4()");

            entity.Property(e => e.trx_invoice_id)
                .HasColumnName("trx_invoice_id")
                .HasMaxLength(50)
                .IsRequired();

            entity.Property(e => e.app_user_id)
                .HasColumnName("app_user_id")
                .HasMaxLength(50);

            entity.Property(e => e.claim_date)
                .HasColumnName("claim_date")
                .HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.Property(e => e.completed_date)
                .HasColumnName("completed_date");

            entity.Property(e => e.claim_media_upload)
                .HasColumnName("claim_media_upload");

            entity.Property(e => e.claim_media_total)
                .HasColumnName("claim_media_total");

            entity.Property(e => e.status)
                .HasColumnName("status")
                .HasMaxLength(50)
                .HasDefaultValue("IN_PROGRESS_SUBMIT")
                .IsRequired();

            entity.Property(e => e.created_by)
                .HasColumnName("created_by")
                .HasMaxLength(50)
                .IsRequired();

            entity.Property(e => e.created_date)
                .HasColumnName("created_date")
                .HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.Property(e => e.updated_by)
                .HasColumnName("updated_by")
                .HasMaxLength(50)
                .IsRequired();

            entity.Property(e => e.updated_date)
                .HasColumnName("updated_date")
                .HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.HasOne(d => d.TrxInvoice)
                .WithMany(p => p.TrxClaims)
                .HasForeignKey(d => d.trx_invoice_id)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("trx_claim_trx_invoice_id_fkey");
        });

        modelBuilder.Entity<trx_claim_detail>(entity =>
        {
            entity.ToTable("trx_claim_detail");

            entity.HasKey(e => e.id).HasName("trx_claim_detail_pkey");

            entity.Property(e => e.id)
                .HasColumnName("id")
                .HasMaxLength(50)
                .HasDefaultValueSql("uuid_generate_v4()");

            entity.Property(e => e.trx_claim_id)
                .HasColumnName("trx_claim_id")
                .HasMaxLength(50)
                .IsRequired();

            entity.Property(e => e.claim_item_type)
                .HasColumnName("claim_item_type")
                .HasMaxLength(100)
                .IsRequired();

            entity.Property(e => e.description)
                .HasColumnName("description");

            entity.Property(e => e.amount)
                .HasColumnName("amount")
                .HasColumnType("numeric(18,2)")
                .HasDefaultValue(0m)
                .IsRequired();

            entity.Property(e => e.created_by)
                .HasColumnName("created_by")
                .HasMaxLength(50)
                .IsRequired();

            entity.Property(e => e.created_date)
                .HasColumnName("created_date")
                .HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.Property(e => e.updated_by)
                .HasColumnName("updated_by")
                .HasMaxLength(50)
                .IsRequired();

            entity.Property(e => e.updated_date)
                .HasColumnName("updated_date")
                .HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.HasOne(d => d.TrxClaim)
                .WithMany(p => p.TrxClaimDetails)
                .HasForeignKey(d => d.trx_claim_id)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("trx_claim_detail_trx_claim_id_fkey");
        });

        modelBuilder.Entity<trx_claim_media>(entity =>
        {
            entity.ToTable("trx_claim_media");

            entity.HasKey(e => e.id).HasName("trx_claim_media_pkey");

            entity.Property(e => e.id)
                .HasColumnName("id")
                .HasMaxLength(50)
                .HasDefaultValueSql("uuid_generate_v4()");

            entity.Property(e => e.trx_claim_id)
                .HasColumnName("trx_claim_id")
                .HasMaxLength(50)
                .IsRequired();

            entity.Property(e => e.claim_item_type)
                .HasColumnName("claim_item_type")
                .HasMaxLength(100)
                .IsRequired();

            entity.Property(e => e.media_type)
                .HasColumnName("media_type")
                .HasMaxLength(100)
                .IsRequired();

            entity.Property(e => e.media_path)
                .HasColumnName("media_path")
                .HasMaxLength(500)
                .IsRequired();

            entity.Property(e => e.created_by)
                .HasColumnName("created_by")
                .HasMaxLength(50)
                .IsRequired();

            entity.Property(e => e.created_date)
                .HasColumnName("created_date")
                .HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.HasOne(d => d.TrxClaim)
                .WithMany(p => p.TrxClaimMedia)
                .HasForeignKey(d => d.trx_claim_id)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("trx_claim_media_trx_claim_id_fkey");
        });
        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
