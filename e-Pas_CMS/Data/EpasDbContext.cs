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

    public virtual DbSet<TrxSurvey> TrxSurveys { get; set; }

    public virtual DbSet<TrxSurveyElement> TrxSurveyElements { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresExtension("uuid-ossp");

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

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
