 terraform {
   required_providers {
     aws = {
       source  = "hashicorp/aws"
       version = "~> 5.0"
     }
   }
 }

 provider "aws" {
   region     = var.aws_region
   access_key = var.aws_access_key_id
   secret_key = var.aws_secret_access_key
 }

 variable "aws_region" {
   type    = string
   default = "us-east-1"  # Cambia esto si necesitas otra región
 }

 variable "bucket_name" {
   type = string
   description = "Nombre único del bucket S3"
 }
 
 variable "aws_access_key_id" {
   type = string
   description = "AWS Access Key ID"
 }
 
 variable "aws_secret_access_key" {
   type = string
   description = "AWS Secret Access Key"
 }

 variable "cloudfront_oai_comment" {
   type    = string
   default = "OAI para CloudFront"
 }

 resource "random_id" "bucket_name_suffix" {
  byte_length = 8
 }

 resource "aws_s3_bucket" "website_bucket" {
    bucket = "${var.bucket_name}-${random_id.bucket_name_suffix.hex}"
    tags = {
      Name = "Bucket para mi sitio web estático"
    }
  }

  resource "aws_s3_bucket_acl" "website_bucket_acl" {
    bucket = aws_s3_bucket.website_bucket.id
    acl    = "public-read"
  }

 resource "aws_s3_bucket_versioning" "versioning" {
    bucket = aws_s3_bucket.website_bucket.id
    versioning_configuration {
      status = "Disabled"  # Puedes habilitar esto si quieres versionado, pero aumentará los costos
    }
  }

 resource "aws_s3_bucket_website_configuration" "website_configuration" {
   bucket = aws_s3_bucket.website_bucket.id

   index_document {
     suffix = "index.html"
   }

   error_document {
     key = "error.html" # Un archivo de error opcional
   }
 }

 resource "aws_s3_bucket_public_access_block" "public_access_block" {
    bucket                  = aws_s3_bucket.website_bucket.id
    block_public_acls       = false # Permitir ACLs públicos
    ignore_public_acls      = false
    block_public_policy     = false  # Permitir políticas públicas
    restrict_public_buckets = false
  }
 resource "aws_cloudfront_origin_access_identity" "cloudfront_oai" {
   comment = var.cloudfront_oai_comment
 }
 data "aws_iam_policy_document" "s3_bucket_policy" {
    statement {
      principals {
        type        = "*"
        identifiers = ["*"]
      }

      actions   = ["s3:GetObject"]
      resources = ["${aws_s3_bucket.website_bucket.arn}/*"]
    }
  }

 resource "aws_s3_bucket_policy" "bucket_policy" {
   bucket = aws_s3_bucket.website_bucket.id
   policy = data.aws_iam_policy_document.s3_bucket_policy.json

   depends_on = [aws_s3_bucket_public_access_block.public_access_block]
 }

 resource "aws_cloudfront_distribution" "cloudfront" {
   origin {
     domain_name = aws_s3_bucket.website_bucket.bucket_regional_domain_name  # Usa bucket_regional_domain_name
     origin_id   = "s3_origin"

     s3_origin_config {
       origin_access_identity = aws_cloudfront_origin_access_identity.cloudfront_oai.cloudfront_access_identity_path
     }
   }
 enabled             = true
   default_root_object = "index.html"
   is_ipv6_enabled     = true

   default_cache_behavior {
     allowed_methods  = ["GET", "HEAD"]
     cached_methods   = ["GET", "HEAD"]
     target_origin_id = "s3_origin"

     forwarded_values {
       query_string = false

       cookies {
         forward = "none"
       }
     }

     viewer_protocol_policy = "redirect-to-https"  # HTTPS
     min_ttl                = 0
     default_ttl            = 3600
     max_ttl                = 86400
   }

   price_class = "PriceClass_100"  # USA, Canada, Europe

   restrictions {
     geo_restriction {
       restriction_type = "none"  # Sin restricciones geográficas
     }
   }

   viewer_certificate {
     cloudfront_default_certificate = true  # Certificado SSL predeterminado de CloudFront
   }
 }

 output "bucket_name_output" {
   value = aws_s3_bucket.website_bucket.bucket
   description = "El nombre del bucket S3 creado."
 }

 output "cloudfront_domain" {
   value = aws_cloudfront_distribution.cloudfront.domain_name
   description = "El nombre de dominio de la distribución de CloudFront."
 }