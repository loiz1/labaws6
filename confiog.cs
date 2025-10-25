using Amazon;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.CloudFront;
using Amazon.CloudFront.Model;
using System;
using System.IO;
using System.Threading.Tasks;

namespace S3CloudFrontSetup
{
    class Program
    {
        // Ya no necesitamos credenciales aquí. Utilizaremos el perfil predeterminado de AWS CLI.
        private const string bucketName = "tu-nombre-unico-de-bucket"; // Debe ser globalmente único
        private const string regionName = "us-east-1"; // O tu región deseada
        private const string indexDocument = "index.html";
        private static readonly RegionEndpoint bucketRegion = RegionEndpoint.GetBySystemName(regionName);
        private static AmazonS3Client s3Client;
        private static AmazonCloudFrontClient cloudFrontClient;
        private static string cloudFrontDistributionId;

        static async Task Main(string[] args)
        {
            // Inicializa los clientes de AWS usando el perfil predeterminado.
            s3Client = new AmazonS3Client(bucketRegion); // Sin credenciales explícitas.
            cloudFrontClient = new AmazonCloudFrontClient(bucketRegion); // Sin credenciales explícitas.

            try
            {
                // 1. Crea el bucket de S3
                await CrearBucketS3Async();

                // 2. Deshabilita el bloqueo de acceso público
                await DeshabilitarBloqueoAccesoPublicoAsync();

                // 3. Configura la política del bucket para acceso público
                await EstablecerPoliticaBucketAsync();

                // 4. Habilita el alojamiento de sitios web estáticos
                await HabilitarAlojamientoSitioWebEstaticoAsync();

                // 5. Crea la distribución de CloudFront
                await CrearDistribucionCloudFrontAsync();

                Console.WriteLine($"Distribución de CloudFront creada. ID de distribución: {cloudFrontDistributionId}");
                Console.WriteLine("Espera a que la distribución se implemente (puede tardar hasta 20 minutos) antes de acceder a tu sitio.");

                // 6. Crea un archivo index.html básico en el directorio del proyecto
                CrearIndexHtmlPredeterminado();

                Console.WriteLine($"Archivo index.html predeterminado creado. Coloca los archivos de tu sitio web en el directorio del proyecto.");
                Console.WriteLine($"Sube los archivos de tu sitio web al bucket usando GitHub Actions (automatizado).");
                Console.WriteLine($"Tu sitio web estará disponible en el nombre de dominio de la distribución de CloudFront: https://{await ObtenerNombreDominioCloudFrontAsync()}");
            }
            catch (AmazonS3Exception e)
            {
                Console.WriteLine($"Error durante las operaciones de S3: {e.Message}");
            }
            catch (AmazonCloudFrontException e)
            {
                Console.WriteLine($"Error durante las operaciones de CloudFront: {e.Message}");
            }
            catch (Exception e)
            {
                Console.WriteLine($"Ocurrió un error: {e.Message}");
            }
            finally
            {
                Console.WriteLine("Presiona cualquier tecla para salir.");
                Console.ReadKey();
            }
        }

        private static async Task CrearBucketS3Async()
        {
            try
            {
                PutBucketRequest putBucketRequest = new PutBucketRequest
                {
                    BucketName = bucketName,
                    UseClientRegion = true,
                    BucketRegion = S3Region.USWest2 // Esta línea podría generar un error si la región no existe como opción
                };

                await s3Client.PutBucketAsync(putBucketRequest);
                Console.WriteLine($"Bucket '{bucketName}' creado con éxito.");
            }
            catch (AmazonS3Exception e)
            {
                // Si el bucket existe (BucketAlreadyOwnedByYou) o la región es incorrecta (InvalidLocationConstraint), informa al usuario.
                if (e.Message.Contains("BucketAlreadyOwnedByYou"))
                {
                    Console.WriteLine($"El bucket '{bucketName}' ya existe. Omitiendo la creación del bucket.");
                }
                else if (e.Message.Contains("InvalidLocationConstraint"))
                {
                    Console.WriteLine($"La región proporcionada no es válida o no admite buckets. Revisa regionName.");
                    throw; // Vuelve a lanzar la excepción para detener el proceso.
                }
                else
                {
                    throw; // Vuelve a lanzar otras AmazonS3Exceptions
                }

            }

        }

        private static async Task DeshabilitarBloqueoAccesoPublicoAsync()
        {
            try
            {
                // Deshabilita la configuración de Bloqueo de acceso público a nivel del bucket
                await s3Client.PutPublicAccessBlockAsync(new PutPublicAccessBlockRequest
                {
                    BucketName = bucketName,
                    PublicAccessBlockConfiguration = new PublicAccessBlockConfiguration
                    {
                        BlockPublicAcls = false,
                        IgnorePublicAcls = false,
                        BlockPublicPolicy = false,
                        RestrictPublicBuckets = false
                    }
                });
                Console.WriteLine($"Bloqueo de acceso público deshabilitado para el bucket '{bucketName}'.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error al deshabilitar el bloqueo de acceso público: {ex.Message}");
                throw; // Vuelve a lanzar para detener la ejecución
            }
        }

        private static async Task EstablecerPoliticaBucketAsync()
        {
            string bucketPolicy = $@"{{
                ""Version"": ""2012-10-17"",
                ""Statement"": [
                    {{
                        ""Sid"": ""PublicReadGetObject"",
                        ""Effect"": ""Allow"",
                        ""Principal"": ""*"",
                        ""Action"": ""s3:GetObject"",
                        ""Resource"": ""arn:aws:s3:::{bucketName}/*""
                    }}
                ]
            }}";

            PutBucketPolicyRequest request = new PutBucketPolicyRequest
            {
                BucketName = bucketName,
                Policy = bucketPolicy
            };

            await s3Client.PutBucketPolicyAsync(request);
            Console.WriteLine($"Política de bucket establecida para permitir el acceso público de lectura para '{bucketName}'.");
        }

        private static async Task HabilitarAlojamientoSitioWebEstaticoAsync()
        {
            try
            {
                PutBucketWebsiteRequest websiteRequest = new PutBucketWebsiteRequest
                {
                    BucketName = bucketName,
                    WebsiteConfiguration = new WebsiteConfiguration
                    {
                        IndexDocument = new IndexDocument { Suffix = indexDocument },
                        ErrorDocument = new ErrorDocument { Key = "error.html" } // Opcional
                    }
                };
                await s3Client.PutBucketWebsiteAsync(websiteRequest);
                Console.WriteLine($"Alojamiento de sitio web estático habilitado para el bucket '{bucketName}'.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error al habilitar el alojamiento de sitio web estático: {ex.Message}");
                throw; // Vuelve a lanzar para detener la ejecución
            }
        }

        private static async Task CrearDistribucionCloudFrontAsync()
        {
            try
            {
                GetCallerIdentityResponse callerIdentity = await new Amazon.SecurityToken.AmazonSecurityTokenServiceClient().GetCallerIdentityAsync(new Amazon.SecurityToken.Model.GetCallerIdentityRequest());
                string accountId = callerIdentity.Account;

                CreateDistributionRequest createDistributionRequest = new CreateDistributionRequest
                {
                    DistributionConfig = new DistributionConfig
                    {
                        CallerReference = Guid.NewGuid().ToString(),
                        Comment = $"Distribución de CloudFront para {bucketName}",
                        DefaultCacheBehavior = new DefaultCacheBehavior
                        {
                            ForwardedValues = new ForwardedValues
                            {
                                QueryString = false, // Establecer en true si necesitas reenviar cadenas de consulta
                                Cookies = new Cookies
                                {
                                    Forward = "none" // "none", "whitelist", "all"
                                }
                            },
                            MinTTL = 0,
                            DefaultTTL = 3600, // 1 hora
                            MaxTTL = 86400,   // 24 horas
                            TargetOriginId = "S3-" + bucketName,
                            ViewerProtocolPolicy = ViewerProtocolPolicy.RedirectToHttps
                        },
                        Enabled = true,
                        Origins = new Origins
                        {
                            Items = new System.Collections.Generic.List<Origin>
                            {
                                new Origin
                                {
                                    DomainName = $"{bucketName}.s3.{bucketRegion.SystemName}.amazonaws.com",
                                    Id = "S3-" + bucketName,
                                    S3OriginConfig = new S3OriginConfig
                                    {
                                        OriginAccessIdentity = $"origin-access-identity/cloudfront/E127KE33O7Z7YI" // Reemplaza con tu ID de OAI si es necesario, o crea uno para un acceso más seguro al bucket.
                                    }
                                }
                            },
                            Quantity = 1
                        },
                        DefaultRootObject = indexDocument, // Importante si tu bucket actúa como un servidor web.
                        PriceClass = PriceClass.PriceClass_100,
                        ViewerCertificate = new ViewerCertificate
                        {
                            CloudFrontDefaultCertificate = true
                        }
                    }
                };

                CreateDistributionResponse createDistributionResponse = await cloudFrontClient.CreateDistributionAsync(createDistributionRequest);
                cloudFrontDistributionId = createDistributionResponse.Distribution.Id;

                Console.WriteLine($"Creación de la distribución de CloudFront iniciada. ID de distribución: {cloudFrontDistributionId}.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error al crear la distribución de CloudFront: {ex.Message}");
                throw; // Vuelve a lanzar para detener la ejecución
            }
        }

        private static void CrearIndexHtmlPredeterminado()
        {
            string htmlContent = "<!DOCTYPE html><html><head><title>¡Hola desde S3!</title></head><body><h1>¡Hola desde S3!</h1><p>Esta es una página predeterminada. Sube los archivos de tu sitio web al bucket de S3.</p></body></html>";
            File.WriteAllText("index.html", htmlContent);
        }

        private static async Task<string> ObtenerNombreDominioCloudFrontAsync()
        {
            try
            {
                GetDistributionRequest distributionRequest = new GetDistributionRequest
                {
                    Id = cloudFrontDistributionId
                };

                GetDistributionResponse distributionResponse = await cloudFrontClient.GetDistributionAsync(distributionRequest);
                return distributionResponse.Distribution.DomainName;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error al obtener el nombre de dominio de la distribución de CloudFront: {ex.Message}");
                return "Error al recuperar el nombre de dominio";
            }
        }
    }
}