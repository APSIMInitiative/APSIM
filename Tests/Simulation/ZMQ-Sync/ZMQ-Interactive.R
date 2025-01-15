library(rzmq)
library(msgpackR)
library(processx)

# Exercise the Interactive protocol

apsimDir <- "/home/uqpdevo1/src/ApsimX-ZMQ"
#apsimDir <- "/usr/local/lib/apsim/"

# Set up a listening server on a random port
testProto2 <- function() {
  apsim <- list()
  
  # The simulation will connect back to this port:
  apsim$apsimSocket <- open_zmq2(port = 0)
  apsim$randomPort <- unlist(strsplit(get.last.endpoint(apsim$apsimSocket),":"))[3]
  cat("Listening on", get.last.endpoint(apsim$apsimSocket), "\n")
  
  apsim$process <- process$new("/usr/bin/dotnet", args=c(
    paste0(apsimDir, "/bin/Debug/net8.0/ApsimZMQServer.dll"), 
    "-p", apsim$randomPort, 
    "-P", "interactive",
    "-f", paste0(apsimDir, "/Tests/Simulation/ZMQ-Sync/ZMQ-sync.apsimx")),
    stdout="", stderr="")

  cat("Started Apsim process id ", apsim$process$get_pid(), "\n")
  return(apsim)
}

# Set up a listening (response) socket that the simulation will connect to
open_zmq2 <- function(port) {
  zcontext <<- init.context()  # This needs to be kept around (global environment)..
  out.socket <- init.socket(zcontext, "ZMQ_REP")
  bind.socket(out.socket, paste0("tcp://0.0.0.0:", port))
  return(out.socket)
}

close_zmq2 <- function(apsim) {
  disconnect.socket(apsim$apsimSocket, get.last.endpoint(apsim$apsimSocket))
  # kill off apsim server
  apsim$process$kill()
}

# Send a command, eg resume/set/get
sendCommand <- function(socket, command, args = NULL) {
  send.raw.string(socket, command, send.more = !is.null(args))
  if (!is.null(args)) {
     for (i in 1:length(args)) {
        send.socket(socket, msgpackR::pack(args[[i]]), serialize = F, send.more = (i != length(args)))
     }
  }
}
# The response loop. When the simulation connects we tell it what to do.
# connect -> ok
# paused -> resume/get/set
# finished -> ok
poll_zmq2 <- function(socket) {
  while (TRUE) {
    msg <- receive.string(socket)
    # cat(msg, "\n")
    if (msg == "connect") {
      sendCommand(socket, "ok")
    } else if (msg == "paused") {
      # Each send is followed by a receive. "set" responds with a string "ok",
      # "get" responds with a packed byte array that we pass to the deserialiser
      sendCommand(socket, "get", "[Clock].Today.Day")
      reply <- msgpackR::unpack(receive.socket(socket, unserialize = F))
      stopifnot(class(reply) == "numeric")

      sendCommand(socket, "get", "[Maize].Phenology.Zadok")
      reply <- msgpackR::unpack(receive.socket(socket, unserialize = F))
      stopifnot(class(reply) == "numeric")
      
      sendCommand(socket, "get", "[Soil].Water.PAW")
      reply <- msgpackR::unpack(receive.socket(socket, unserialize = F))
      stopifnot(class(reply) == "numeric" && length(reply) == 7 )

      sendCommand(socket, "get", "[Manager].Script.DummyStringVar")
      reply1 <- msgpackR::unpack(receive.socket(socket, unserialize = F))

      sendCommand(socket, "get", "[Nutrient].NO3.kgha")
      reply1 <- msgpackR::unpack(receive.socket(socket, unserialize = F))
      #cat("r1 = ", reply1, "\n")
      
      # This is working, but not ideal
      #sendCommand(socket, "setNO3", 42.0)
      #msg <- receive.string(socket)

      # This is the same but a bit better
      sendCommand(socket, "set", list("[Nutrient].NO3.kgha", 2 * reply1))
      msg <- receive.string(socket)

      sendCommand(socket, "get", "[Nutrient].NO3.kgha")
      reply2 <- msgpackR::unpack(receive.socket(socket, unserialize = F))
      #cat("r2 = ", reply2, "\n")
      
      sendCommand(socket, "set", list("[Nutrient].NO3.kgha", reply1))
      msg <- receive.string(socket)

      sendCommand(socket, "get", "[Nutrient].NO3.kgha")
      reply3 <- msgpackR::unpack(receive.socket(socket, unserialize = F))
      #cat("r3 = ", reply3, "\n")
      sendCommand(socket, "set", list("[Manager].Script.DummyStringVar", "Blork"))
      msg <- receive.string(socket)

      sendCommand(socket, "get", "[Manager].Script.DummyStringVar")
      reply <- msgpackR::unpack(receive.socket(socket, unserialize = F))

      reply1 <- paste(sample(LETTERS, nchar(reply)), collapse="")

      sendCommand(socket, "set", list("[Manager].Script.DummyStringVar", reply1))
      msg <- receive.string(socket)
      
      sendCommand(socket, "set", list("[Manager].Script.DummyDoubleVar", 42.42))
      msg <- receive.string(socket)
      # cat("set result = ", msg, "\n")
      # resume the simulation and come back tomorrow
      sendCommand(socket, "resume")
    } else if (msg == "finished") {
      sendCommand(socket, "ok")
      break
    } 
  }
}

apsim <- testProto2()
rec <- data.frame(iter = 1:10, mem=NA)

for (i in 1:nrow(rec)) {
  start_time <- Sys.time()
  poll_zmq2(apsim$apsimSocket)
  rec$mem[i] <- apsim$process$get_memory_info()[["rss"]]
  rec$time[i] <-  Sys.time() - start_time
}

close_zmq2(apsim)

print(rec)
