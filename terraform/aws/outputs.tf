output "alb_dns_name" {
  value       = aws_lb.app.dns_name
  description = "DNS name of the application load balancer"
}

output "postgres_endpoint" {
  value       = aws_rds_cluster.postgres.endpoint
  description = "Aurora Postgres endpoint"
}
