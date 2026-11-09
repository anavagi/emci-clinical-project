using Microsoft.EntityFrameworkCore;
using EmciDemo.Models;

namespace EmciDemo.Data
{
    public static class DbContextEmciExtensions
    {
        public static void ConfigureEmciModel(this ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<ClinicalDocumentRecord>(entity =>
            {
                entity.ToTable("ClinicalDocumentRecords");
                entity.HasKey(e => e.Id);
                entity.Property(e => e.PatientId).IsRequired();
                entity.Property(e => e.TemplateCode).IsRequired().HasMaxLength(100);
                entity.Property(e => e.Status).IsRequired().HasMaxLength(50);
                entity.Property(e => e.PayloadJson).IsRequired();

                entity.HasIndex(e => e.PatientId);
                entity.HasIndex(e => e.EpisodeId);
                entity.HasIndex(e => e.Status);
                entity.HasIndex(e => e.TemplateCode);
            });
        }
    }
}
