library(rzmq)
library(msgpackR)
library(processx)

# Exercise "One-shot" protocol

apsimDir <- "/home/uqpdevo1/src/ApsimX-ZMQ"
#apsimDir <- "/usr/local/lib/apsim/"

# Open a request socket 
open_zmq1 <- function(port) {
  zcontext <<- init.context()  # This needs to be kept around (global environment)..
  out.socket <- init.socket(zcontext, "ZMQ_REQ")
  connect.socket(out.socket, paste0("tcp://127.0.0.1:", port))
  return(out.socket)
}

# Send a "run" command. Args are "Changes" - name=value pairs that each set an
# apsim variable in a simulation. 
# Blocks until simulation completes
run_zmq1 <- function (socket, args) {
  send.socket(socket, charToRaw("RUN"), serialize = F,  send.more = T)
  send.socket(socket, charToRaw(paste(args, collapse="\n")), serialize = F, send.more = F)
  message <- rawToChar(receive.socket(socket, unserialize = F))
  return(message)
}

# Get columns from the datastore after the simulation has finished.
get_zmq1 <- function (socket, table, args) {
  result <- list()
  for (v in args) {
    send.socket(socket, charToRaw("GET"), serialize = F, send.more = T)
    send.socket(socket, charToRaw(paste0(table, ".", v)), serialize = F, send.more = F)
    msg <- receive.socket(socket, unserialize = F)
    #cat("msg:", msg, " bytes\n")
    result[[v]] <- msgpackR::unpack(msg)
  }
  return(as.data.frame(result))
}

# Close this connection, shut down the server
close_zmq1 <- function(socket) {
  disconnect.socket(socket, get.last.endpoint(socket))
  # kill off apsim server
  apsim$process$kill()
}


# Set up an apsim server process
testProto1 <- function() {
  apsim <- list()
  
  # Find a random unused port:
  apsim$randomPort <- system("bash -c \"comm -23 <(seq 49152 65535 | sort) <(ss -Htan | awk '{print $4}' | cut -d':' -f2 | sort -u) | shuf | head -n 1\"", 
                             intern=T, ignore.stderr=T)
  
  # FIXME: should let the server find an ephemeral port, then it should tell us
  apsim$process <- process$new("/usr/bin/dotnet", args=c(
    paste0(apsimDir, "/bin/Debug/net8.0/ApsimZMQServer.dll"), 
    "-p", apsim$randomPort, 
    "-P", "oneshot",
    "-a", "127.0.0.1",
    "-f", paste0(apsimDir, "/Examples/Wheat.apsimx")),
    stdout="", stderr="")
  
  if (! apsim$process$is_alive() ) { stop("Didnt start apsim?") }
  
  apsim$apsimSocket <- open_zmq1(port = apsim$randomPort)
  return(apsim)
}

apsim <- testProto1()

run_zmq1(apsim$apsimSocket, "[Sow using a variable rule].Script.CultivarName = Batavia")

df <- get_zmq1(apsim$apsimSocket, "Report", c("[Clock].Today", "[Wheat].LAI"))
str(df)

close_zmq1(apsim$apsimSocket)
